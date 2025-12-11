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
    public partial class ShelvedStateMachineState
    {
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            UpdateStateVariable(context, Objects.ShelvedStateMachineType_Unshelved, CurrentState);
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
            new(Objects.ShelvedStateMachineType_OneShotShelved, BrowseNames.OneShotShelve, 1),
            new(Objects.ShelvedStateMachineType_TimedShelved, BrowseNames.TimedShelved, 2),
            new(Objects.ShelvedStateMachineType_Unshelved, BrowseNames.Unshelved, 3)
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
                Objects.ShelvedStateMachineType_OneShotShelvedToTimedShelved,
                BrowseNames.OneShotShelvedToTimedShelved,
                1
            ),
            new(
                Objects.ShelvedStateMachineType_OneShotShelvedToUnshelved,
                BrowseNames.OneShotShelvedToUnshelved,
                2),
            new(
                Objects.ShelvedStateMachineType_TimedShelvedToOneShotShelved,
                BrowseNames.TimedShelvedToOneShotShelved,
                3
            ),
            new(
                Objects.ShelvedStateMachineType_TimedShelvedToUnshelved,
                BrowseNames.TimedShelvedToUnshelved,
                4),
            new(
                Objects.ShelvedStateMachineType_UnshelvedToOneShotShelved,
                BrowseNames.UnshelvedToOneShotShelved,
                5),
            new(
                Objects.ShelvedStateMachineType_UnshelvedToTimedShelved,
                BrowseNames.UnshelvedToTimedShelved,
                6)
        ];

        /// <summary>
        /// The mapping between transitions and their from and to states.
        /// </summary>
        protected override uint[,] TransitionMappings => s_transitionMappings;

        /// <summary>
        /// A table of the to and from states for the transitions.
        /// </summary>
        private static readonly uint[,] s_transitionMappings = new uint[,]
        {
            {
                Objects.ShelvedStateMachineType_OneShotShelvedToTimedShelved,
                Objects.ShelvedStateMachineType_OneShotShelved,
                Objects.ShelvedStateMachineType_TimedShelved,
                0
            },
            {
                Objects.ShelvedStateMachineType_OneShotShelvedToUnshelved,
                Objects.ShelvedStateMachineType_OneShotShelved,
                Objects.ShelvedStateMachineType_Unshelved,
                1
            },
            {
                Objects.ShelvedStateMachineType_TimedShelvedToOneShotShelved,
                Objects.ShelvedStateMachineType_TimedShelved,
                Objects.ShelvedStateMachineType_OneShotShelved,
                0
            },
            {
                Objects.ShelvedStateMachineType_TimedShelvedToUnshelved,
                Objects.ShelvedStateMachineType_TimedShelved,
                Objects.ShelvedStateMachineType_Unshelved,
                1
            },
            {
                Objects.ShelvedStateMachineType_UnshelvedToOneShotShelved,
                Objects.ShelvedStateMachineType_Unshelved,
                Objects.ShelvedStateMachineType_OneShotShelved,
                1
            },
            {
                Objects.ShelvedStateMachineType_UnshelvedToTimedShelved,
                Objects.ShelvedStateMachineType_Unshelved,
                Objects.ShelvedStateMachineType_TimedShelved,
                1
            }
        };

        /// <summary>
        /// The mapping between causes, the current state and a transition.
        /// </summary>
        protected override uint[,] CauseMappings => s_causeMappings;

        /// <summary>
        /// A table of transitions for the available causes.
        /// </summary>
        private static readonly uint[,] s_causeMappings = new uint[,]
        {
            {
                Methods.ShelvedStateMachineType_TimedShelve,
                Objects.ShelvedStateMachineType_OneShotShelved,
                Objects.ShelvedStateMachineType_OneShotShelvedToTimedShelved
            },
            {
                Methods.ShelvedStateMachineType_Unshelve,
                Objects.ShelvedStateMachineType_OneShotShelved,
                Objects.ShelvedStateMachineType_OneShotShelvedToUnshelved
            },
            {
                Methods.ShelvedStateMachineType_OneShotShelve,
                Objects.ShelvedStateMachineType_TimedShelved,
                Objects.ShelvedStateMachineType_TimedShelvedToOneShotShelved
            },
            {
                Methods.ShelvedStateMachineType_Unshelve,
                Objects.ShelvedStateMachineType_TimedShelved,
                Objects.ShelvedStateMachineType_TimedShelvedToUnshelved
            },
            {
                Methods.ShelvedStateMachineType_OneShotShelve,
                Objects.ShelvedStateMachineType_Unshelved,
                Objects.ShelvedStateMachineType_UnshelvedToOneShotShelved
            },
            {
                Methods.ShelvedStateMachineType_TimedShelve,
                Objects.ShelvedStateMachineType_Unshelved,
                Objects.ShelvedStateMachineType_UnshelvedToTimedShelved
            }
        };
    }
}
