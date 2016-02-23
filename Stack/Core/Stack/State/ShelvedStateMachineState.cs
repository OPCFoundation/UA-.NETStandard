/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Text;

namespace Opc.Ua
{
    partial class ShelvedStateMachineState
    {
        #region Initialization
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            UpdateStateVariable(context, Objects.ShelvedStateMachineType_Unshelved, CurrentState);
            UpdateTransitionVariable(context, 0, LastTransition);
        }
        #endregion

        #region Overriden Members
        /// <summary>
        /// The table of states belonging to the state machine.
        /// </summary>
        protected override ElementInfo[] StateTable
        {
            get { return s_StateTable; }
        }

        /// <summary>
        /// A table of valid states.
        /// </summary>
        private ElementInfo[] s_StateTable = new ElementInfo[]
        {
            new ElementInfo(Objects.ShelvedStateMachineType_OneShotShelved, BrowseNames.OneShotShelve, 1),
            new ElementInfo(Objects.ShelvedStateMachineType_TimedShelved, BrowseNames.TimedShelved, 2),
            new ElementInfo(Objects.ShelvedStateMachineType_Unshelved, BrowseNames.Unshelved, 3)
        };

        /// <summary>
        /// The table of transitions belonging to the state machine.
        /// </summary>
        protected override ElementInfo[] TransitionTable
        {
            get { return s_TransitionTable; }
        }

        /// <summary>
        /// A table of valid transitions.
        /// </summary>
        private ElementInfo[] s_TransitionTable = new ElementInfo[]
        {
            new ElementInfo(Objects.ShelvedStateMachineType_OneShotShelvedToTimedShelved, BrowseNames.OneShotShelvedToTimedShelved, 1),
            new ElementInfo(Objects.ShelvedStateMachineType_OneShotShelvedToUnshelved, BrowseNames.OneShotShelvedToUnshelved, 2),
            new ElementInfo(Objects.ShelvedStateMachineType_TimedShelvedToOneShotShelved, BrowseNames.TimedShelvedToOneShotShelved, 3),
            new ElementInfo(Objects.ShelvedStateMachineType_TimedShelvedToUnshelved, BrowseNames.TimedShelvedToUnshelved, 4),
            new ElementInfo(Objects.ShelvedStateMachineType_UnshelvedToOneShotShelved, BrowseNames.UnshelvedToOneShotShelved, 5),
            new ElementInfo(Objects.ShelvedStateMachineType_UnshelvedToTimedShelved, BrowseNames.UnshelvedToTimedShelved, 6),
        };

        /// <summary>
        /// The mapping between transitions and their from and to states.
        /// </summary>
        protected override uint[,] TransitionMappings
        {
            get { return s_TransitionMappings; }
        }

        /// <summary>
        /// A table of the to and from states for the transitions.
        /// </summary>
        private uint[,] s_TransitionMappings = new uint[,]
        {
            { Objects.ShelvedStateMachineType_OneShotShelvedToTimedShelved, Objects.ShelvedStateMachineType_OneShotShelved, Objects.ShelvedStateMachineType_TimedShelved, 0 },
            { Objects.ShelvedStateMachineType_OneShotShelvedToUnshelved, Objects.ShelvedStateMachineType_OneShotShelved, Objects.ShelvedStateMachineType_Unshelved, 1 },
            { Objects.ShelvedStateMachineType_TimedShelvedToOneShotShelved, Objects.ShelvedStateMachineType_TimedShelved, Objects.ShelvedStateMachineType_OneShotShelved, 0 },
            { Objects.ShelvedStateMachineType_TimedShelvedToUnshelved, Objects.ShelvedStateMachineType_TimedShelved, Objects.ShelvedStateMachineType_Unshelved, 1 },
            { Objects.ShelvedStateMachineType_UnshelvedToOneShotShelved, Objects.ShelvedStateMachineType_Unshelved, Objects.ShelvedStateMachineType_OneShotShelved, 1 },
            { Objects.ShelvedStateMachineType_UnshelvedToTimedShelved, Objects.ShelvedStateMachineType_Unshelved, Objects.ShelvedStateMachineType_TimedShelved, 1 },
        };

        /// <summary>
        /// The mapping between causes, the current state and a transition.
        /// </summary>
        protected override uint[,] CauseMappings
        {
            get { return s_CauseMappings; }
        }

        /// <summary>
        /// A table of transitions for the available causes.
        /// </summary>
        private uint[,] s_CauseMappings = new uint[,]
        {
            { Methods.ShelvedStateMachineType_TimedShelve, Objects.ShelvedStateMachineType_OneShotShelved, Objects.ShelvedStateMachineType_OneShotShelvedToTimedShelved },
            { Methods.ShelvedStateMachineType_Unshelve, Objects.ShelvedStateMachineType_OneShotShelved, Objects.ShelvedStateMachineType_OneShotShelvedToUnshelved },
            { Methods.ShelvedStateMachineType_OneShotShelve, Objects.ShelvedStateMachineType_TimedShelved, Objects.ShelvedStateMachineType_TimedShelvedToOneShotShelved },
            { Methods.ShelvedStateMachineType_Unshelve, Objects.ShelvedStateMachineType_TimedShelved, Objects.ShelvedStateMachineType_TimedShelvedToUnshelved },
            { Methods.ShelvedStateMachineType_OneShotShelve, Objects.ShelvedStateMachineType_Unshelved, Objects.ShelvedStateMachineType_UnshelvedToOneShotShelved },
            { Methods.ShelvedStateMachineType_TimedShelve, Objects.ShelvedStateMachineType_Unshelved, Objects.ShelvedStateMachineType_UnshelvedToTimedShelved },
        };
        #endregion
    }
}
