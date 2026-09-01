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
    /// <para>
    /// Element NodeIds come from the machine's own
    /// <see cref="FiniteStateMachineState.GetStateNodeId"/> /
    /// <see cref="FiniteStateMachineState.GetTransitionNodeId"/>, so a generated machine writes
    /// ids in its model namespace and a machine that materializes its own element nodes points
    /// at the real node. That requires the machine to have completed its create lifecycle —
    /// <c>ElementNamespaceUri</c> is resolved in <c>OnAfterCreate</c> — so a node assembled by a
    /// <c>CreateInstanceOf</c> factory must be passed through
    /// <see cref="NodeState.CreateAsPredefinedNode(ISystemContext)"/> first. <c>namespaceIndex</c> now only
    /// backstops reading a state variable some other component wrote.
    /// </para>
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
            // Materialize the optional LastTransition now, while the
            // machine is typically still pre-registration — deferring
            // it to the first ApplyTransition would mint nodes into an
            // already-indexed address space, where clients can browse
            // but not read them.
            EnsureLastTransition(machine, context);
            ClearLastTransition(machine, context);
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

            if (machine.CurrentState.Id != null)
            {
                // The machine owns the element-NodeId convention:
                // generated machines qualify the numeric id with their
                // model namespace, and machines that materialize their
                // own state nodes point at the real node.
                machine.CurrentState.Id.Value = machine.GetStateNodeId(stateId);
            }
            if (machine.CurrentState.Number != null)
            {
                machine.CurrentState.Number.Value = entry.Number;
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

            if (machine.LastTransition.Id != null)
            {
                machine.LastTransition.Id.Value =
                    machine.GetTransitionNodeId(transitionId);
            }
            if (machine.LastTransition.Number != null)
            {
                machine.LastTransition.Number.Value = entry.Number;
            }
            if (machine.LastTransition.TransitionTime != null)
            {
                machine.LastTransition.TransitionTime.Value = DateTime.UtcNow;
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
            if (nodeId.IsNull)
            {
                return false;
            }

            // Ask the machine first — it owns the mapping — then fall
            // back to the numeric convention for state variables a
            // caller wrote in some other namespace.
            uint id = machine.GetStateId(nodeId);
            if (id != 0)
            {
                stateId = id;
                return true;
            }
            if (nodeId.NamespaceIndex != m_namespaceIndex)
            {
                return false;
            }
            if (nodeId.TryGetValue(out uint numericId))
            {
                stateId = numericId;
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

        private static void ClearLastTransition(
            FiniteStateMachineState machine,
            ISystemContext context)
        {
            if (machine?.LastTransition is null)
            {
                return;
            }

            machine.LastTransition.Value = LocalizedText.Null;

            if (machine.LastTransition.Id != null)
            {
                machine.LastTransition.Id.Value = NodeId.Null;
            }
            if (machine.LastTransition.Number != null)
            {
                machine.LastTransition.Number.Value = 0;
            }
            if (machine.LastTransition.TransitionTime != null)
            {
                machine.LastTransition.TransitionTime.Value = DateTime.MinValue;
            }

            machine.LastTransition.ClearChangeMasks(context, includeChildren: true);
        }

        private static void EnsureLastTransition(
            FiniteStateMachineState machine,
            ISystemContext context)
        {
            if (machine.LastTransition is null)
            {
                machine.AddLastTransition(context);
            }

            // ApplyTransition writes the optional Number child, so it
            // has to exist by now too — created later it would land in
            // an already-indexed address space.
            machine.LastTransition!.AddNumber(context);
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
