// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Interprets an application's committed event data on each replica.
    /// </summary>
    public interface IReplicatedEventSource
    {
        /// <summary>
        /// Materializes an ordinary event for a registered local notifier.
        /// </summary>
        BaseEventState CreateEvent(ISystemContext context, NodeState notifier, in Variant payload);

        /// <summary>
        /// Applies a committed application update to the specified condition or branch.
        /// </summary>
        /// <remarks>
        /// Use the same deterministic interpretation on every replica. Do not separately report the event.
        /// </remarks>
        void ApplyCondition(ISystemContext context, AcknowledgeableConditionState condition, in Variant payload);

        /// <summary>
        /// Authorizes the operation on the exact live trunk or branch, using the original client comment.
        /// </summary>
        /// <remarks>
        /// Runs after standard Method authorization and native condition-state validation, before committing.
        /// Do not mutate state or perform external side effects. Migrate any native acknowledgement or
        /// confirmation policy to this callback before removing the original hooks and binding the condition.
        /// </remarks>
        ServiceResult AuthorizeConditionMethod(
            ISystemContext context,
            AcknowledgeableConditionState condition,
            ReplicatedConditionMethod method,
            ByteString eventId,
            LocalizedText comment);
    }
}
