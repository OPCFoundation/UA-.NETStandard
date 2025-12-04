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

namespace Opc.Ua
{
    public partial class ExclusiveLimitStateMachineState
    {
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            UpdateStateVariable(context, Objects.ExclusiveLimitStateMachineType_High, CurrentState);
            UpdateTransitionVariable(context, 0, LastTransition);
        }

        /// <summary>
        /// The table of states belonging to the state machine.
        /// </summary>
        protected override ElementInfo[] StateTable => s_stateTable;

        /// <summary>
        /// A table of valid states.
        /// </summary>
        private static readonly ElementInfo[] s_stateTable =
        [
            new(Objects.ExclusiveLimitStateMachineType_HighHigh, BrowseNames.HighHigh, 1),
            new(Objects.ExclusiveLimitStateMachineType_High, BrowseNames.High, 2),
            new(Objects.ExclusiveLimitStateMachineType_Low, BrowseNames.Low, 3),
            new(Objects.ExclusiveLimitStateMachineType_LowLow, BrowseNames.LowLow, 4)
        ];

        /// <summary>
        /// The table of transitions belonging to the state machine.
        /// </summary>
        protected override ElementInfo[] TransitionTable => s_transitionTable;

        /// <summary>
        /// A table of valid transitions.
        /// </summary>
        private static readonly ElementInfo[] s_transitionTable =
        [
            new(
                Objects.ExclusiveLimitStateMachineType_HighHighToHigh,
                BrowseNames.HighHighToHigh,
                1),
            new(
                Objects.ExclusiveLimitStateMachineType_HighToHighHigh,
                BrowseNames.HighToHighHigh,
                2),
            new(Objects.ExclusiveLimitStateMachineType_LowLowToLow, BrowseNames.LowLowToLow, 3),
            new(Objects.ExclusiveLimitStateMachineType_LowToLowLow, BrowseNames.LowToLowLow, 4)
        ];

        /// <summary>
        /// The mapping between transitions and their from and to states.
        /// </summary>
        protected override uint[,] TransitionMappings => m_transitionMappings;

        /// <summary>
        /// A table of the to and from states for the transitions.
        /// </summary>
        private readonly uint[,] m_transitionMappings = new uint[,]
        {
            {
                Objects.ExclusiveLimitStateMachineType_HighHighToHigh,
                Objects.ExclusiveLimitStateMachineType_HighHigh,
                Objects.ExclusiveLimitStateMachineType_High,
                0
            },
            {
                Objects.ExclusiveLimitStateMachineType_HighToHighHigh,
                Objects.ExclusiveLimitStateMachineType_High,
                Objects.ExclusiveLimitStateMachineType_HighHigh,
                0
            },
            {
                Objects.ExclusiveLimitStateMachineType_LowLowToLow,
                Objects.ExclusiveLimitStateMachineType_LowLow,
                Objects.ExclusiveLimitStateMachineType_Low,
                0
            },
            {
                Objects.ExclusiveLimitStateMachineType_LowToLowLow,
                Objects.ExclusiveLimitStateMachineType_Low,
                Objects.ExclusiveLimitStateMachineType_LowLow,
                0
            }
        };
    }
}
