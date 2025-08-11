/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
            new(Objects.ShelvedStateMachineType_Unshelved, BrowseNames.Unshelved, 3),
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
            new(Objects.ShelvedStateMachineType_OneShotShelvedToUnshelved, BrowseNames.OneShotShelvedToUnshelved, 2),
            new(
                Objects.ShelvedStateMachineType_TimedShelvedToOneShotShelved,
                BrowseNames.TimedShelvedToOneShotShelved,
                3
            ),
            new(Objects.ShelvedStateMachineType_TimedShelvedToUnshelved, BrowseNames.TimedShelvedToUnshelved, 4),
            new(Objects.ShelvedStateMachineType_UnshelvedToOneShotShelved, BrowseNames.UnshelvedToOneShotShelved, 5),
            new(Objects.ShelvedStateMachineType_UnshelvedToTimedShelved, BrowseNames.UnshelvedToTimedShelved, 6),
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
                0,
            },
            {
                Objects.ShelvedStateMachineType_OneShotShelvedToUnshelved,
                Objects.ShelvedStateMachineType_OneShotShelved,
                Objects.ShelvedStateMachineType_Unshelved,
                1,
            },
            {
                Objects.ShelvedStateMachineType_TimedShelvedToOneShotShelved,
                Objects.ShelvedStateMachineType_TimedShelved,
                Objects.ShelvedStateMachineType_OneShotShelved,
                0,
            },
            {
                Objects.ShelvedStateMachineType_TimedShelvedToUnshelved,
                Objects.ShelvedStateMachineType_TimedShelved,
                Objects.ShelvedStateMachineType_Unshelved,
                1,
            },
            {
                Objects.ShelvedStateMachineType_UnshelvedToOneShotShelved,
                Objects.ShelvedStateMachineType_Unshelved,
                Objects.ShelvedStateMachineType_OneShotShelved,
                1,
            },
            {
                Objects.ShelvedStateMachineType_UnshelvedToTimedShelved,
                Objects.ShelvedStateMachineType_Unshelved,
                Objects.ShelvedStateMachineType_TimedShelved,
                1,
            },
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
                Objects.ShelvedStateMachineType_OneShotShelvedToTimedShelved,
            },
            {
                Methods.ShelvedStateMachineType_Unshelve,
                Objects.ShelvedStateMachineType_OneShotShelved,
                Objects.ShelvedStateMachineType_OneShotShelvedToUnshelved,
            },
            {
                Methods.ShelvedStateMachineType_OneShotShelve,
                Objects.ShelvedStateMachineType_TimedShelved,
                Objects.ShelvedStateMachineType_TimedShelvedToOneShotShelved,
            },
            {
                Methods.ShelvedStateMachineType_Unshelve,
                Objects.ShelvedStateMachineType_TimedShelved,
                Objects.ShelvedStateMachineType_TimedShelvedToUnshelved,
            },
            {
                Methods.ShelvedStateMachineType_OneShotShelve,
                Objects.ShelvedStateMachineType_Unshelved,
                Objects.ShelvedStateMachineType_UnshelvedToOneShotShelved,
            },
            {
                Methods.ShelvedStateMachineType_TimedShelve,
                Objects.ShelvedStateMachineType_Unshelved,
                Objects.ShelvedStateMachineType_UnshelvedToTimedShelved,
            },
        };
    }
}
