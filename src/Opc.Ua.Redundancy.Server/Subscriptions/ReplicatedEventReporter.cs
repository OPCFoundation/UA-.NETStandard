// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Identifies one occurrence allocated by the authoritative application event source.
    /// </summary>
    /// <param name="GroupId">The stable UUID of the redundant server set.</param>
    /// <param name="OccurrenceId">The unique UUID allocated once for this occurrence in the set.</param>
    public readonly record struct ReplicatedEventIdentity(Uuid GroupId, Uuid OccurrenceId);

    /// <summary>
    /// A condition method that must execute at the authority for a mirrored condition.
    /// </summary>
    public enum ReplicatedConditionMethod
    {
        /// <summary>
        /// Acknowledge the specified occurrence.
        /// </summary>
        Acknowledge,

        /// <summary>
        /// Confirm the specified occurrence.
        /// </summary>
        Confirm
    }

    /// <summary>
    /// Relays condition method calls to the trusted authority for mirrored condition state.
    /// </summary>
    public interface IReplicatedConditionMethodRelay
    {
        /// <summary>
        /// Authorizes and commits a condition method at the source of truth. The source resolves the
        /// supplied EventId to its exact occurrence and branch, and commits any resulting occurrences
        /// to the same ordered feed as ordinary condition state changes.
        /// </summary>
        /// <param name="groupId">The redundant server set for the call.</param>
        /// <param name="context">The local client call context, including its user identity.</param>
        /// <param name="conditionId">The condition's stable address-space identifier.</param>
        /// <param name="method">The requested condition method.</param>
        /// <param name="eventId">The client's canonical EventId.</param>
        /// <param name="comment">The client comment.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The authoritative method result, or a bad result if the authority is unavailable.</returns>
        ValueTask<ServiceResult> CallAsync(
            Uuid groupId,
            ISystemContext context,
            NodeId conditionId,
            ReplicatedConditionMethod method,
            ByteString eventId,
            LocalizedText comment,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Delivers authoritative occurrences to the native event and condition pipeline of one replica.
    /// </summary>
    /// <remarks>
    /// Each replica must consume the same authenticated, committed occurrence feed. The feed assigns
    /// occurrence UUIDs, retains condition state and method associations, and delivers updates once
    /// in order to each replica. This class neither transports records nor infers occurrences from event fields.
    /// </remarks>
    public sealed class ReplicatedEventReporter
    {
        /// <summary>
        /// Creates a reporter for a Transparent or HotAndMirrored redundant server set.
        /// </summary>
        /// <param name="mode">The server's configured redundancy mode.</param>
        /// <param name="groupId">The stable redundant-server-set UUID shared by every member.</param>
        /// <param name="relay">The authoritative condition method relay.</param>
        public ReplicatedEventReporter(
            RedundancySupport mode,
            Uuid groupId,
            IReplicatedConditionMethodRelay relay)
        {
            if (mode is not (RedundancySupport.Transparent or RedundancySupport.HotAndMirrored))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            if (groupId == Guid.Empty)
            {
                throw new ArgumentException("The redundant server set UUID must not be empty.", nameof(groupId));
            }
            m_groupId = groupId;
            m_groupBytes = groupId.ToByteString();
            m_relay = relay ?? throw new ArgumentNullException(nameof(relay));
        }

        /// <summary>
        /// Gets the exact client-visible ID for an occurrence of this group.
        /// </summary>
        /// <param name="identity">The identity from the authoritative feed.</param>
        /// <returns>The 16 group UUID bytes followed by the 16 occurrence UUID bytes in UA binary GUID order.</returns>
        public ByteString GetEventId(ReplicatedEventIdentity identity)
        {
            if (identity.GroupId != m_groupId || identity.OccurrenceId == Guid.Empty)
            {
                throw new ArgumentException("Expected a non-empty occurrence UUID for this group.", nameof(identity));
            }
            return ByteString.Combine(m_groupBytes, identity.OccurrenceId.ToByteString());
        }

        /// <summary>
        /// Reports a committed ordinary event through the local notifier without modifying the source event.
        /// </summary>
        /// <remarks>Custom event types must preserve their fields when cloned.</remarks>
        /// <param name="context">The local system context.</param>
        /// <param name="notifier">The local node that reports the event.</param>
        /// <param name="eventState">The event payload supplied by the feed, in local namespace indices.</param>
        /// <param name="identity">The committed occurrence identity.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask ReportEventAsync(
            ISystemContext context,
            NodeState notifier,
            BaseEventState eventState,
            ReplicatedEventIdentity identity,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (notifier == null)
            {
                throw new ArgumentNullException(nameof(notifier));
            }
            if (eventState == null)
            {
                throw new ArgumentNullException(nameof(eventState));
            }
            if (eventState is ConditionState)
            {
                throw new ArgumentException("Use ApplyConditionOccurrenceAsync for a condition.", nameof(eventState));
            }

            cancellationToken.ThrowIfCancellationRequested();
            ByteString eventId = GetEventId(identity);
            if (eventState.Clone() is not BaseEventState snapshot || snapshot.GetType() != eventState.GetType())
            {
                throw new ArgumentException("The event must clone to the same event type.", nameof(eventState));
            }
            snapshot.EventId ??= PropertyState<ByteString>.With<VariantBuilder>(snapshot);
            snapshot.EventId.Value = eventId;
            return notifier.ReportEventAsync(context, snapshot, cancellationToken);
        }

        /// <summary>
        /// Installs a committed condition state and its EventId; optionally reports it to local subscribers.
        /// </summary>
        /// <remarks>
        /// Apply committed updates in order, serialized with other changes to the same condition. Replay
        /// condition state on a new standby before serving requests. Do not assign EventId or BranchId
        /// in <paramref name="apply"/>. Use the native state setters for acknowledgement and confirmation
        /// transitions and supply authoritative timestamps and Retain values. Validate the incoming
        /// state before applying it; the callback mutates live state and does not roll back on failure.
        /// Ensure the application does not independently generate condition state at other replicas.
        /// </remarks>
        /// <param name="context">The local system context.</param>
        /// <param name="condition">The root condition node on this replica.</param>
        /// <param name="branchId">The source's stable branch NodeId in the local namespace table, or null.</param>
        /// <param name="identity">The committed occurrence identity.</param>
        /// <param name="apply">Applies the committed attributes to the resolved trunk or branch.</param>
        /// <param name="publish">Whether to deliver the occurrence to local event subscribers.</param>
        /// <param name="cancellationToken">Cancellation is observed before changing the condition.</param>
        public ValueTask ApplyConditionOccurrenceAsync(
            ISystemContext context,
            ConditionState condition,
            NodeId branchId,
            ReplicatedEventIdentity identity,
            Action<ConditionState> apply,
            bool publish = true,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }
            if (apply == null)
            {
                throw new ArgumentNullException(nameof(apply));
            }

            cancellationToken.ThrowIfCancellationRequested();
            InstanceStateSnapshot snapshot = condition.ApplyReplicatedOccurrence(
                context, branchId, GetEventId(identity), apply);
            return publish ? condition.ReportEventAsync(context, snapshot, CancellationToken.None) : default;
        }

        /// <summary>
        /// Applies the source's retirement of a branch after its final state has been delivered.
        /// </summary>
        /// <param name="condition">The root condition on this replica.</param>
        /// <param name="branchId">The retired branch's stable ID in the local namespace table.</param>
        /// <returns>Whether a live branch was removed.</returns>
        public bool RetireConditionBranch(ConditionState condition, NodeId branchId)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }
            return condition.RemoveReplicatedBranch(branchId);
        }

        /// <summary>
        /// Replaces the native local Acknowledge and Confirm handlers with calls to the trusted authority.
        /// </summary>
        /// <remarks>
        /// Bind initialized, addressable condition trunks before they serve requests. Optional Confirm
        /// must be materialized before binding. Migrate existing native Acknowledge and Confirm callbacks
        /// to the trusted authority before clearing the callbacks and binding. The application must relay
        /// or disable other mutating methods and must not run local automatic condition logic.
        /// </remarks>
        /// <param name="condition">The condition owned by the authoritative event source.</param>
        public void BindConditionMethods(AcknowledgeableConditionState condition)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }
            if (condition.NodeId.IsNull || condition.BranchId is not { Value.IsNull: true } ||
                condition.Acknowledge is not { } acknowledge)
            {
                throw new ArgumentException("Expected an initialized, addressable condition trunk.", nameof(condition));
            }
            if (condition.OnAcknowledge != null || condition.OnConfirm != null)
            {
                throw new InvalidOperationException(
                    "Migrate native Acknowledge and Confirm callbacks to the trusted authority before binding.");
            }
            AddCommentMethodState? confirm = condition.Confirm;
            if (HasAsyncBinding(acknowledge) || HasAsyncBinding(confirm))
            {
                throw new InvalidOperationException("The condition already has an asynchronous method binding.");
            }

            condition.AutoReportStateChanges = false;
            BindConditionMethod(acknowledge, condition, ReplicatedConditionMethod.Acknowledge);
            if (confirm != null)
            {
                BindConditionMethod(confirm, condition, ReplicatedConditionMethod.Confirm);
            }
        }

        private static bool HasAsyncBinding(AddCommentMethodState? method)
        {
            return method?.OnCallAsync != null || method?.OnCallMethodWithResultAsync != null ||
                method?.OnCallMethod2Async != null;
        }

        private void BindConditionMethod(
            AddCommentMethodState node,
            AcknowledgeableConditionState condition,
            ReplicatedConditionMethod method)
        {
            node.OnCall = (_, _, _, _, _) => StatusCodes.BadNotSupported;
            node.OnCallAsync = async (context, _, _, eventId, comment, ct) =>
            {
                DateTime calledAt = DateTime.UtcNow;
                ServiceResult result;
                try
                {
                    result = await RelayConditionMethodAsync(context, condition.NodeId, method, eventId, comment, ct)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException ex)
                {
                    result = ex.Result;
                }
                if (context is ServerSystemContext { Server: { Auditing: true } server })
                {
                    await ReportConditionMethodAuditAsync(
                        server, context, condition, node, method, eventId, comment, calledAt, result)
                        .ConfigureAwait(false);
                }
                return new AddCommentMethodStateResult { ServiceResult = result };
            };
        }

        private static ValueTask ReportConditionMethodAuditAsync(
            IServerInternal server,
            ISystemContext context,
            AcknowledgeableConditionState condition,
            MethodState node,
            ReplicatedConditionMethod method,
            ByteString eventId,
            LocalizedText comment,
            DateTime calledAt,
            ServiceResult result)
        {
            bool confirm = method == ReplicatedConditionMethod.Confirm;
            AuditEventState audit = confirm ? new AuditConditionConfirmEventState(null) :
                new AuditConditionAcknowledgeEventState(null);
            LocalizedText message = confirm ? LocalizedText.From("The Confirm method was called.") :
                LocalizedText.From("The Acknowledge method was called.");
            audit.Initialize(context, condition, EventSeverity.Low, message, ServiceResult.IsGood(result), calledAt);
            audit.SetChildValue(
                context, BrowseNames.SourceName, confirm ? "Method/Confirm" : "Method/Acknowledge", false);
            audit.SetChildValue(context, BrowseNames.MethodId, node.NodeId, false);
            audit.SetChildValue(context, BrowseNames.InputArguments, Variant.From(new Variant[] { eventId, comment }),
                false);
            audit.SetChildValue(context, BrowseNames.ConditionEventId, eventId, false);
            audit.SetChildValue(context, BrowseNames.Comment, comment, false);
            return server.ReportEventAsync(context, audit);
        }

        private ValueTask<ServiceResult> RelayConditionMethodAsync(
            ISystemContext context,
            NodeId conditionId,
            ReplicatedConditionMethod method,
            ByteString eventId,
            LocalizedText comment,
            CancellationToken cancellationToken)
        {
            if (eventId.Length != 32 || !eventId.Span[..16].SequenceEqual(m_groupBytes.Span))
            {
                return new ValueTask<ServiceResult>(StatusCodes.BadEventIdUnknown);
            }
            return m_relay.CallAsync(m_groupId, context, conditionId, method, eventId, comment, cancellationToken);
        }

        private readonly Uuid m_groupId;
        private readonly ByteString m_groupBytes;
        private readonly IReplicatedConditionMethodRelay m_relay;
    }
}
