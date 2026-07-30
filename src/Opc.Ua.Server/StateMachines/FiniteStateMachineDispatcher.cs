/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;

namespace Opc.Ua.Server.StateMachines
{
    /// <summary>
    /// Well-known metadata for one finite-state-machine state or transition.
    /// </summary>
    /// <param name="Id">
    /// The numeric identifier in the state machine namespace.
    /// </param>
    /// <param name="Number">
    /// The StateNumber or TransitionNumber value.
    /// </param>
    /// <param name="Name">
    /// The display name written to the state variable.
    /// </param>
    public readonly record struct FiniteStateMachineEntry(uint Id, uint Number, string Name);

    /// <summary>
    /// Writes Part 16 CurrentState and LastTransition variables for generated state machines.
    /// </summary>
    /// <remarks>
    /// Source-generated companion state machines do not currently emit StateTable and TransitionTable overrides.
    /// This dispatcher updates the standard state variables directly while preserving the generated node hierarchy.
    /// </remarks>
    public sealed class FiniteStateMachineDispatcher
    {
        private readonly ushort m_namespaceIndex;
        private readonly ArrayOf<FiniteStateMachineEntry> m_states;
        private readonly ArrayOf<FiniteStateMachineEntry> m_transitions;

        /// <summary>
        /// Initializes a new instance of the <see cref="FiniteStateMachineDispatcher"/> class.
        /// </summary>
        public FiniteStateMachineDispatcher(
            ushort namespaceIndex,
            ArrayOf<FiniteStateMachineEntry> states,
            ArrayOf<FiniteStateMachineEntry> transitions)
        {
            m_namespaceIndex = namespaceIndex;
            m_states = states;
            m_transitions = transitions;
        }

        /// <summary>
        /// Initializes the current state and clears any previous transition.
        /// </summary>
        public void InitializeToInitialState(
            FiniteStateMachineState machine,
            uint stateId,
            ISystemContext context)
        {
            ApplyState(machine, stateId, context);
            ClearLastTransition(machine);
        }

        /// <summary>
        /// Writes CurrentState for the specified state identifier.
        /// </summary>
        public void ApplyState(
            FiniteStateMachineState machine,
            uint stateId,
            ISystemContext context)
        {
            if (machine?.CurrentState is null)
            {
                return;
            }

            FiniteStateMachineEntry entry = Lookup(m_states, stateId, "Unknown");
            machine.CurrentState.AddNumber(context);
            machine.CurrentState.Value = new LocalizedText(entry.Name);

            if (machine.CurrentState.Id is { } idVariable)
            {
                idVariable.Value = new NodeId(stateId, m_namespaceIndex);
            }
            if (machine.CurrentState.Number is { } numberVariable)
            {
                numberVariable.Value = entry.Number;
            }

            machine.CurrentState.ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Writes LastTransition for the specified transition identifier.
        /// </summary>
        public void ApplyTransition(
            FiniteStateMachineState machine,
            uint transitionId,
            ISystemContext context)
        {
            if (machine is null)
            {
                return;
            }

            EnsureLastTransition(machine, context);
            if (machine.LastTransition is null)
            {
                return;
            }

            FiniteStateMachineEntry entry = Lookup(m_transitions, transitionId, "Unknown");
            machine.LastTransition.AddNumber(context);
            machine.LastTransition.Value = new LocalizedText(entry.Name);

            if (machine.LastTransition.Id is { } idVariable)
            {
                idVariable.Value = new NodeId(transitionId, m_namespaceIndex);
            }
            if (machine.LastTransition.Number is { } numberVariable)
            {
                numberVariable.Value = entry.Number;
            }
            if (machine.LastTransition.TransitionTime is { } transitionTimeVariable)
            {
                transitionTimeVariable.Value = DateTime.UtcNow;
            }

            machine.LastTransition.ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Writes CurrentState and LastTransition for one completed move.
        /// </summary>
        public void Move(
            FiniteStateMachineState machine,
            uint toStateId,
            uint transitionId,
            ISystemContext context)
        {
            ApplyState(machine, toStateId, context);
            ApplyTransition(machine, transitionId, context);
        }

        /// <summary>
        /// Attempts to read the numeric current state identifier.
        /// </summary>
        public bool TryGetCurrentState(FiniteStateMachineState machine, out uint stateId)
        {
            stateId = 0;
            if (machine?.CurrentState?.Id == null)
            {
                return false;
            }

            NodeId nodeId = machine.CurrentState.Id.Value;
            if (nodeId.IsNull || nodeId.NamespaceIndex != m_namespaceIndex)
            {
                return false;
            }

            if (nodeId.TryGetValue(out uint id))
            {
                stateId = id;
                return true;
            }
            if (uint.TryParse(nodeId.IdentifierAsString, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out uint parsedId))
            {
                stateId = parsedId;
                return true;
            }

            return false;
        }

        private static void ClearLastTransition(FiniteStateMachineState machine)
        {
            if (machine?.LastTransition is null)
            {
                return;
            }

            machine.LastTransition.Value = LocalizedText.Null;

            if (machine.LastTransition.Id is { } idVariable)
            {
                idVariable.Value = NodeId.Null;
            }
            if (machine.LastTransition.Number is { } numberVariable)
            {
                numberVariable.Value = 0;
            }
            if (machine.LastTransition.TransitionTime is { } transitionTimeVariable)
            {
                transitionTimeVariable.Value = DateTime.MinValue;
            }
        }

        private static void EnsureLastTransition(
            FiniteStateMachineState machine,
            ISystemContext context)
        {
            if (machine.LastTransition is not null)
            {
                return;
            }

            machine.AddLastTransition(context);
        }

        private static FiniteStateMachineEntry Lookup(
            ArrayOf<FiniteStateMachineEntry> table,
            uint id,
            string fallbackName)
        {
            for (int ii = 0; ii < table.Count; ii++)
            {
                if (table[ii].Id == id)
                {
                    return table[ii];
                }
            }

            return new FiniteStateMachineEntry(id, 0, fallbackName);
        }
    }
}
