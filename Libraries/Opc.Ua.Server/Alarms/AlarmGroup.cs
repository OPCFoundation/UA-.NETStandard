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

namespace Opc.Ua.Server.Alarms
{
    /// <summary>
    /// Tracks members of an <see cref="AlarmGroupState"/> and exposes
    /// utility methods to enumerate and update group membership.
    /// </summary>
    public sealed class AlarmGroup
    {
        private readonly AlarmGroupState m_state;

        /// <summary>
        /// The wrapped <see cref="AlarmGroupState"/> node.
        /// </summary>
        public AlarmGroupState State => m_state;

        /// <summary>
        /// The NodeId of the group.
        /// </summary>
        public NodeId NodeId => m_state.NodeId;

        /// <summary>
        /// Initializes a new alarm group helper wrapping the given state.
        /// </summary>
        public AlarmGroup(AlarmGroupState state)
        {
            m_state = state ?? throw new System.ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// Adds an alarm condition to this group using the
        /// <c>AlarmGroupMember</c> reference type.
        /// </summary>
        public void AddMember(AlarmConditionState alarm)
        {
            if (alarm == null)
            {
                throw new ArgumentNullException(nameof(alarm));
            }
            m_state.AddReference(ReferenceTypeIds.AlarmGroupMember, false, alarm.NodeId);
            alarm.AddReference(ReferenceTypeIds.AlarmGroupMember, true, m_state.NodeId);
        }

        /// <summary>
        /// Removes an alarm condition from this group.
        /// </summary>
        public void RemoveMember(AlarmConditionState alarm)
        {
            if (alarm == null)
            {
                throw new ArgumentNullException(nameof(alarm));
            }
            m_state.RemoveReference(ReferenceTypeIds.AlarmGroupMember, false, alarm.NodeId);
            alarm.RemoveReference(ReferenceTypeIds.AlarmGroupMember, true, m_state.NodeId);
        }

        /// <summary>
        /// Enumerates the NodeIds of all alarms in this group.
        /// </summary>
        public IEnumerable<NodeId> GetMemberIds(ISystemContext context)
        {
            var references = new List<IReference>();
            m_state.GetReferences(context, references,
                ReferenceTypeIds.AlarmGroupMember, false);

            foreach (IReference reference in references)
            {
                NodeId targetId = ExpandedNodeId.ToNodeId(
                    reference.TargetId, context.NamespaceUris);
                if (!targetId.IsNull)
                {
                    yield return targetId;
                }
            }
        }
    }
}
