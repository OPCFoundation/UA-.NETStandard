/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
        /// <summary>
        /// The wrapped <see cref="AlarmGroupState"/> node.
        /// </summary>
        public AlarmGroupState State { get; }

        /// <summary>
        /// The NodeId of the group.
        /// </summary>
        public NodeId NodeId => State.NodeId;

        /// <summary>
        /// Initializes a new alarm group helper wrapping the given state.
        /// </summary>
        public AlarmGroup(AlarmGroupState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// Adds an alarm condition to this group using the
        /// <c>AlarmGroupMember</c> reference type.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="alarm"/> is <c>null</c>.</exception>
        public void AddMember(AlarmConditionState alarm)
        {
            if (alarm == null)
            {
                throw new ArgumentNullException(nameof(alarm));
            }
            State.AddReference(ReferenceTypeIds.AlarmGroupMember, false, alarm.NodeId);
            alarm.AddReference(ReferenceTypeIds.AlarmGroupMember, true, State.NodeId);
        }

        /// <summary>
        /// Removes an alarm condition from this group.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="alarm"/> is <c>null</c>.</exception>
        public void RemoveMember(AlarmConditionState alarm)
        {
            if (alarm == null)
            {
                throw new ArgumentNullException(nameof(alarm));
            }
            State.RemoveReference(ReferenceTypeIds.AlarmGroupMember, false, alarm.NodeId);
            alarm.RemoveReference(ReferenceTypeIds.AlarmGroupMember, true, State.NodeId);
        }

        /// <summary>
        /// Enumerates the NodeIds of all alarms in this group.
        /// </summary>
        public IEnumerable<NodeId> GetMemberIds(ISystemContext context)
        {
            var references = new List<IReference>();
            State.GetReferences(context, references,
                ReferenceTypeIds.AlarmGroupMember, false);

            foreach (IReference reference in references)
            {
                var targetId = ExpandedNodeId.ToNodeId(
                    reference.TargetId, context.NamespaceUris);
                if (!targetId.IsNull)
                {
                    yield return targetId;
                }
            }
        }
    }
}
