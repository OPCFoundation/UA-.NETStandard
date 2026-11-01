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

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// The current state of a state machine: <c>DeviceState</c>,
    /// <c>FunctionalUnitState</c>, <c>RunningStateMachine</c>,
    /// <c>ControlFunctionState</c>, <c>CoverState</c>, <c>MaintenanceState</c>
    /// or the Machinery item and operation mode states.
    /// </summary>
    /// <param name="StateMachineId">The NodeId of the state machine.</param>
    /// <param name="CurrentState">The display text of the current state.</param>
    /// <param name="CurrentStateId">The NodeId of the current state, when the server exposes it.</param>
    public sealed record LadsStateSnapshot(NodeId StateMachineId, string CurrentState, NodeId CurrentStateId)
    {
        /// <summary>
        /// Gets whether the current state has the given name, ignoring case.
        /// </summary>
        /// <param name="stateName">The state name, for example <c>Running</c>.</param>
        public bool IsIn(string stateName)
        {
            return string.Equals(CurrentState, stateName, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return CurrentState;
        }
    }
}
