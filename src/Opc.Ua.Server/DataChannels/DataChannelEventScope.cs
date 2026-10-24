/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Runtime.CompilerServices;

namespace Opc.Ua.Server.DataChannels
{
    /// <summary>
    /// Attaches non-serializable recipient policy to ordinary immutable event snapshots.
    /// Authorization runs before EventFilter evaluation and again before publication.
    /// </summary>
    internal sealed class DataChannelEventScope(Func<ISession, bool> visible)
    {
        /// <summary>
        /// Creates a standard snapshot so existing type/source ReceiveEvents permission checks still apply.
        /// </summary>
        internal static IFilterTarget Create(
            ISystemContext context,
            BaseEventState state,
            Func<ISession, bool> visible)
        {
            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(context, state);
            // Standard event initializers replace these fields with value-only PropertyStates.
            // Preserve their standard browse names in the immutable snapshot used for EventFilter selection.
            snapshot.SetChildValue(QualifiedName.From(BrowseNames.EventId), NodeClass.Variable, state.EventId!.Value);
            snapshot.SetChildValue(
                QualifiedName.From(BrowseNames.EventType), NodeClass.Variable, state.EventType!.Value);
            snapshot.SetChildValue(
                QualifiedName.From(BrowseNames.SourceNode), NodeClass.Variable, state.SourceNode!.Value);
            snapshot.SetChildValue(
                QualifiedName.From(BrowseNames.SourceName), NodeClass.Variable, state.SourceName!.Value);
            snapshot.SetChildValue(QualifiedName.From(BrowseNames.Time), NodeClass.Variable, state.Time!.Value);
            snapshot.SetChildValue(
                QualifiedName.From(BrowseNames.ReceiveTime), NodeClass.Variable, state.ReceiveTime!.Value);
            snapshot.SetChildValue(QualifiedName.From(BrowseNames.Message), NodeClass.Variable, state.Message!.Value);
            snapshot.SetChildValue(QualifiedName.From(BrowseNames.Severity), NodeClass.Variable, state.Severity!.Value);
            if (state is AuditEventState audit)
            {
                snapshot.SetChildValue(
                    QualifiedName.From(BrowseNames.ActionTimeStamp), NodeClass.Variable, audit.ActionTimeStamp!.Value);
                snapshot.SetChildValue(QualifiedName.From(BrowseNames.Status), NodeClass.Variable, audit.Status!.Value);
                snapshot.SetChildValue(
                    QualifiedName.From(BrowseNames.ServerId), NodeClass.Variable, audit.ServerId!.Value);
                snapshot.SetChildValue(
                    QualifiedName.From(BrowseNames.ClientAuditEntryId),
                    NodeClass.Variable,
                    audit.ClientAuditEntryId!.Value);
                snapshot.SetChildValue(
                    QualifiedName.From(BrowseNames.ClientUserId), NodeClass.Variable, audit.ClientUserId!.Value);
            }
            if (state is AuditUpdateMethodEventState method)
            {
                snapshot.SetChildValue(
                    QualifiedName.From(BrowseNames.MethodId), NodeClass.Variable, method.MethodId!.Value);
                snapshot.SetChildValue(
                    QualifiedName.From(BrowseNames.InputArguments), NodeClass.Variable, method.InputArguments!.Value);
                if (method.StatusCodeId != null)
                {
                    snapshot.SetChildValue(
                        QualifiedName.From(BrowseNames.StatusCodeId), NodeClass.Variable, method.StatusCodeId.Value);
                }
                if (method.OutputArguments != null)
                {
                    snapshot.SetChildValue(
                        QualifiedName.From(BrowseNames.OutputArguments),
                        NodeClass.Variable,
                        method.OutputArguments.Value);
                }
            }
            s_scopes.Add(snapshot, new DataChannelEventScope(visible));
            return snapshot;
        }

        /// <summary>
        /// Rejects unauthorized scoped events before any fields can be selected or queued.
        /// Unscoped Data Channel events fail closed.
        /// </summary>
        internal static bool IsVisible(IFilterTarget target, ISession? session)
        {
            if (s_scopes.TryGetValue(target, out DataChannelEventScope? scope))
            {
                return session != null && scope.m_visible(session);
            }
            BaseEventState? state = target as BaseEventState;
            if (target is InstanceStateSnapshot snapshot)
            {
                state = snapshot.Handle as BaseEventState;
            }
            return state is not (DataChannelOfferedEventState or DataChannelStateChangeEventState or
                AuditDataChannelEventState or DataChannelOperationEventState);
        }

        private static readonly ConditionalWeakTable<IFilterTarget, DataChannelEventScope> s_scopes = new();
        private readonly Func<ISession, bool> m_visible = visible;
    }
}
