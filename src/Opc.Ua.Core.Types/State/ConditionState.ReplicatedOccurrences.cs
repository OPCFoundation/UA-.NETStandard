// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua
{
    public partial class ConditionState
    {
        /// <summary>
        /// Installs a committed occurrence on the trunk or on a branch identified by its stable BranchId.
        /// </summary>
        /// <remarks>The caller serializes application of committed records for the condition.</remarks>
        internal InstanceStateSnapshot ApplyReplicatedOccurrence(
            ISystemContext context,
            NodeId branchId,
            ByteString eventId,
            Action<ConditionState> apply)
        {
            if (BranchId is not { Value.IsNull: true } || EventId == null || AutoReportStateChanges)
            {
                throw new InvalidOperationException(
                    "Apply replicated occurrences to the condition trunk without automatic reporting.");
            }
            if (ResolveEventId(eventId) != null)
            {
                throw new InvalidOperationException("The condition already reported the replicated EventId.");
            }

            ConditionState? existing = branchId.IsNull ? this : FindReplicatedBranch(branchId);
            ConditionState target = existing ?? CreateReplicatedBranch(context, branchId);
            apply(target);

            target.BranchId!.Value = branchId;
            target.EventId!.Value = eventId;
            if (!ReferenceEquals(target, this))
            {
                RekeyBranch(target);
            }

            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(context, target);
            target.RecordReportedEvent(snapshot);
            return snapshot;
        }

        /// <summary>
        /// Removes a retired branch from ConditionRefresh and the live condition method associations.
        /// </summary>
        internal bool RemoveReplicatedBranch(NodeId branchId)
        {
            if (branchId.IsNull || FindReplicatedBranch(branchId) is not { } branch)
            {
                return false;
            }

            RemoveBranch(branch);
            return true;
        }

        /// <summary>
        /// Resolves an exact live occurrence for a condition acknowledgement or confirmation.
        /// </summary>
        internal ServiceResult ValidateReplicatedMethod(
            ByteString eventId,
            bool confirm,
            out AcknowledgeableConditionState? target)
        {
            target = null;
            if (eventId.IsEmpty)
            {
                return StatusCodes.BadEventIdUnknown;
            }
            if (EnabledState?.Id?.Value != true)
            {
                return StatusCodes.BadConditionDisabled;
            }
            if (ResolveEventId(eventId) is not { Owner: AcknowledgeableConditionState owner } reported)
            {
                return StatusCodes.BadEventIdUnknown;
            }
            if (!confirm)
            {
                if (owner.AckedState?.Id?.Value == true || reported.AcknowledgedSince)
                {
                    return StatusCodes.BadConditionBranchAlreadyAcked;
                }
            }
            else
            {
                if (!owner.SupportsConfirm())
                {
                    return StatusCodes.BadMethodInvalid;
                }
                if (owner.ConfirmedState?.Id?.Value == true || reported.ConfirmedSince)
                {
                    return StatusCodes.BadConditionBranchAlreadyConfirmed;
                }
            }
            if (!reported.IsLive)
            {
                return StatusCodes.BadEventIdUnknown;
            }
            if (owner.EnabledState?.Id?.Value != true)
            {
                return StatusCodes.BadConditionDisabled;
            }
            target = owner;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Recalculates the trunk Retain state after applying a committed branch command.
        /// </summary>
        internal void RefreshReplicatedRetain()
        {
            UpdateRetainState();
        }

        private ConditionState? FindReplicatedBranch(NodeId branchId)
        {
            lock (m_branchesLock)
            {
                if (m_branches != null)
                {
                    foreach (ConditionState branch in m_branches.Values)
                    {
                        if (branch.BranchId!.Value == branchId)
                        {
                            return branch;
                        }
                    }
                }
            }
            return null;
        }

        private ConditionState CreateReplicatedBranch(ISystemContext context, NodeId branchId)
        {
            ConditionState branch = CreateBranchInstance() ??
                throw new InvalidOperationException("This condition type cannot create a branch.");
            branch.Initialize(context, this);
            branch.BranchId!.Value = branchId;
            branch.RetainStateProvider = RetainStateProvider;
            CopySupportsFilteredRetain(context, branch);
            return branch;
        }
    }
}
