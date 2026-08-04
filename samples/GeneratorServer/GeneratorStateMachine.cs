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
using Opc.Ua;
using Opc.Ua.Generators;
using Opc.Ua.Server;
using Opc.Ua.Server.StateMachines;

namespace Generators
{
    /// <summary>
    /// Publishes the operating state machine and the generator-set methods.
    /// </summary>
    public partial class GeneratorNodeManager
    {
        /// <summary>
        /// Attaches the operating state machine and the methods to one set.
        /// </summary>
        /// <param name="set">The generator set.</param>
        /// <param name="simulation">The set's simulation.</param>
        private void AttachStateMachine(GeneratorSetState set, GeneratorSimulation simulation)
        {
            GeneratorStateMachineState? machine = set.OperatingState;
            if (machine == null)
            {
                return;
            }

            // The state machine already exists - it is mandatory on the type - so
            // this is lifecycle mode: attach behaviour rather than define states.
            StateMachineBuilder.For(machine, SystemContext)
                .WithInitialState(GeneratorStateMachineTypeIds.StateNumbers.Off);

            PublishState(machine, GeneratorRunState.Off);
            PublishOperatingMode(set, GeneratorOperatingModeEnum.Auto);

            // The simulation decides; the address space follows.
            simulation.StateChanged = (from, to) =>
            {
                if (GeneratorStateMap.TransitionNumbers.TryGetValue((from, to), out uint transition))
                {
                    machine.DoTransition(SystemContext, transition, 0, default, []);
                }
                PublishState(machine, to);

                // Shutdown alarms latch, so something has to release them. Leaving a
                // shutdown state is that moment, whether the operator got there with
                // ResetFaults or the set recovered on its own - otherwise an
                // unattended plant accumulates alarms that are annunciated forever on
                // machines that are running again.
                if (to == GeneratorRunState.Off &&
                    from is GeneratorRunState.Fault or GeneratorRunState.EmergencyStopped)
                {
                    ClearLatchedAlarms(set);
                }

                m_logger.GeneratorStateChanged(set.NodeId, to);
            };

            AttachMethods(set, simulation);
        }

        /// <summary>
        /// Writes the current state onto the state machine's CurrentState variable.
        /// </summary>
        /// <param name="machine">The set's state machine.</param>
        /// <param name="state">The state now current.</param>
        private void PublishState(GeneratorStateMachineState machine, GeneratorRunState state)
        {
            if (machine.CurrentState == null ||
                !GeneratorStateMap.StateNumbers.TryGetValue(state, out uint number))
            {
                return;
            }

            // CurrentState carries the human-readable name; its Id property carries
            // the state node a client actually compares against, so both are written
            // or a client sees a name it cannot resolve.
            machine.CurrentState.Value = new LocalizedText(state.ToString());
            PropertyState<NodeId>? id = machine.CurrentState.CreateOrReplaceId(SystemContext, null!);
            if (id != null)
            {
                id.Value = NodeId.Create(
                    number,
                    Opc.Ua.Generators.Namespaces.Generators,
                    Server.NamespaceUris);
                id.ClearChangeMasks(SystemContext, false);
            }
            machine.CurrentState.ClearChangeMasks(SystemContext, false);
        }

        /// <summary>
        /// Writes the control-panel selector position.
        /// </summary>
        /// <param name="set">The generator set.</param>
        /// <param name="mode">The mode to publish.</param>
        private void PublishOperatingMode(GeneratorSetState set, GeneratorOperatingModeEnum mode)
        {
            if (set.OperatingMode == null)
            {
                return;
            }
            set.OperatingMode.Value = mode;
            set.OperatingMode.ClearChangeMasks(SystemContext, false);
        }

        /// <summary>
        /// Wires the six generator-set methods to the simulation.
        /// </summary>
        /// <param name="set">The generator set.</param>
        /// <param name="simulation">The set's simulation.</param>
        /// <remarks>
        /// Legality lives in the simulation rather than in each handler, so a caller
        /// cannot drive a machine from Off straight to Loaded by picking the right
        /// method. A refused request answers <c>BadInvalidState</c> instead of
        /// silently doing nothing.
        /// </remarks>
        private void AttachMethods(GeneratorSetState set, GeneratorSimulation simulation)
        {
            Bind(set.Start, (args, outs) => GeneratorCommands.Start(simulation));

            Bind(set.Stop, (args, outs) => GeneratorCommands.Stop(simulation));

            Bind(set.EmergencyStop, (args, outs) => GeneratorCommands.EmergencyStop(simulation));

            Bind(set.ResetFaults, (args, outs) =>
            {
                ClearLatchedAlarms(set);
                return GeneratorCommands.ResetFaults(simulation);
            });

            Bind(set.StartTest, (args, outs) =>
            {
                PublishOperatingMode(set, GeneratorOperatingModeEnum.Test);
                return GeneratorCommands.StartTest(simulation);
            });

            Bind(set.SetOperatingMode, (args, outs) =>
            {
                if (!GeneratorCommands.TryReadOperatingMode(
                        args, out GeneratorOperatingModeEnum mode))
                {
                    return false;
                }
                PublishOperatingMode(set, mode);
                return true;
            });
        }

        /// <summary>
        /// Attaches a call handler that reports a refused request as
        /// <c>BadInvalidState</c>.
        /// </summary>
        /// <param name="method">The method to bind, when present.</param>
        /// <param name="handler">Returns whether the request was accepted.</param>
        /// <remarks>
        /// Every handler runs under <see cref="m_simulationGate"/>, so a command and
        /// the simulation tick cannot transition the same set at once. Taking the
        /// gate here rather than in each handler means a handler added later cannot
        /// forget it.
        /// </remarks>
        private void Bind(
            MethodState? method,
            Func<ArrayOf<Variant>, List<Variant>, bool> handler)
        {
            if (method == null)
            {
                return;
            }
            method.OnCallMethod2 = (ctx, m, objectId, inputs, outputs) =>
            {
                bool accepted;
                lock (m_simulationGate)
                {
                    accepted = handler(inputs, outputs);
                }
                return accepted ? ServiceResult.Good : StatusCodes.BadInvalidState;
            };
        }
    }
}
