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
    partial class ExclusiveLimitStateMachineState
    {
        #region Initialization
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            UpdateStateVariable(context, Objects.ExclusiveLimitStateMachineType_High, CurrentState);
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
            new ElementInfo(Objects.ExclusiveLimitStateMachineType_HighHigh, BrowseNames.HighHigh, 1),
            new ElementInfo(Objects.ExclusiveLimitStateMachineType_High, BrowseNames.High, 2),
            new ElementInfo(Objects.ExclusiveLimitStateMachineType_Low, BrowseNames.Low, 3),
            new ElementInfo(Objects.ExclusiveLimitStateMachineType_LowLow, BrowseNames.LowLow, 4)
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
            new ElementInfo(Objects.ExclusiveLimitStateMachineType_HighHighToHigh, BrowseNames.HighHighToHigh, 1),
            new ElementInfo(Objects.ExclusiveLimitStateMachineType_HighToHighHigh, BrowseNames.HighToHighHigh, 2),
            new ElementInfo(Objects.ExclusiveLimitStateMachineType_LowLowToLow, BrowseNames.LowLowToLow, 3),
            new ElementInfo(Objects.ExclusiveLimitStateMachineType_LowToLowLow, BrowseNames.LowToLowLow, 4)
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
            { Objects.ExclusiveLimitStateMachineType_HighHighToHigh, Objects.ExclusiveLimitStateMachineType_HighHigh, Objects.ExclusiveLimitStateMachineType_High, 0 },
            { Objects.ExclusiveLimitStateMachineType_HighToHighHigh, Objects.ExclusiveLimitStateMachineType_High, Objects.ExclusiveLimitStateMachineType_HighHigh, 0 },
            { Objects.ExclusiveLimitStateMachineType_LowLowToLow, Objects.ExclusiveLimitStateMachineType_LowLow, Objects.ExclusiveLimitStateMachineType_Low, 0 },
            { Objects.ExclusiveLimitStateMachineType_LowToLowLow, Objects.ExclusiveLimitStateMachineType_Low, Objects.ExclusiveLimitStateMachineType_LowLow, 0 }
        };
        #endregion
    }
}
