/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Binds one machine's operator-visible notifications to the standard alert and condition machinery.
    /// The containing runtime owns this controller and all of its nodes; the provider remains application-owned.
    /// </summary>
    public sealed partial class MachineToolNotificationController : IAsyncDisposable
    {
        /// <summary>
        /// Creates the unpublished notification binding without copying a companion's Messages subtype.
        /// </summary>
        internal MachineToolNotificationController(
            IMachineryBuildContext context, MachineToolState machine,
            MachineToolNotificationOptions options, TimeProvider timeProvider)
        {
            m_context = context;
            m_machine = machine;
            m_options = options;
            m_timeProvider = timeProvider;
            NotificationState notification = machine.Notification ??
                throw Invalid("The generated machine is missing Notification.");
            var name = new QualifiedName(BrowseNames.Messages,
                (ushort)context.Context.NamespaceUris.GetIndex(Namespaces.MachineTool));
            State = (notification.FindChild(context.Context, name) ??
                notification.CreateChild(context.Context, name)) as MessagesState ??
                throw Invalid("The generated machine does not declare MessagesType.");
            Link(machine, notification);
            Link(notification, State);
        }

        /// <summary>
        /// Gets the actual generated Messages notifier, including an application subtype.
        /// </summary>
        public MessagesState State { get; }

        /// <summary>
        /// Gets the last accepted provider inventory, not a fabricated condition history.
        /// </summary>
        public MachineToolNotificationSnapshot? Observation { get; private set; }

        /// <summary>
        /// Gets whether this async flow is inside the provider or event publication callback.
        /// </summary>
        internal bool IsInCallback => ReferenceEquals(s_callback.Value, this);

        /// <summary>
        /// Gets whether this binding has stopped accepting operations.
        /// </summary>
        internal bool IsDisposed => m_stopped;

        /// <summary>
        /// Resolves an exposed condition by its stable provider identity.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is <c>null</c>.</exception>
        public bool TryGetAlert(string key, [NotNullWhen(true)] out AlertState? alert)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            lock (m_lifetimeGate)
            {
                if (m_alerts.TryGetValue(key, out AlertBinding? binding))
                {
                    alert = binding.State;
                    return true;
                }
            }
            alert = null;
            return false;
        }

        /// <summary>
        /// Initializes validated conditions while the containing machine is still unpublished.
        /// </summary>
        internal async ValueTask InitializeAsync(
            MachineToolNotificationSnapshot snapshot, CancellationToken cancellationToken)
        {
            m_messageType = new BaseObjectTypeState
            {
                NodeId = new NodeId(Guid.NewGuid(), m_context.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("MachineToolMessage", m_context.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("MachineTool notification"),
                SuperTypeId = ExpandedNodeId.ToNodeId(ObjectTypeIds.NotificationEventType,
                    m_context.Context.NamespaceUris),
                IsAbstract = false,
                IsPartOfTypeHierarchy = true
            };
            await m_context.Manager.AddPredefinedNodeAsync(m_messageType, cancellationToken).ConfigureAwait(false);
            await ApplyAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Acquires one complete bounded notification update and retries any accepted unfinished publication first.
        /// </summary>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource timeout =
                m_timeProvider.CreateCancellationTokenSource(m_options.RefreshTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using CancellationTokenSource operation = BeginOperation(linked.Token);
            MachineToolNotificationController? previous = s_callback.Value;
            s_callback.Value = this;
            bool entered = false;
            try
            {
                await m_update.WaitAsync(operation.Token).ConfigureAwait(false);
                entered = true;
                await PublishPendingAsync(operation.Token).ConfigureAwait(false);
                MachineToolNotificationSnapshot snapshot = await m_options.Provider!
                    .ReadAsync(m_sequence, operation.Token).ConfigureAwait(false);
                ValidateSnapshot(m_context, m_options, snapshot, m_sequence);
                operation.Token.ThrowIfCancellationRequested();
                await ApplyAsync(snapshot, operation.Token).ConfigureAwait(false);
                await PublishPendingAsync(operation.Token).ConfigureAwait(false);
            }
            finally
            {
                if (entered)
                {
                    m_update.Release();
                }
                s_callback.Value = previous;
                EndOperation();
            }
        }

        /// <summary>
        /// Publishes the accepted snapshots with stable event identities and bounded retry state.
        /// </summary>
        internal async ValueTask PublishPendingAsync(CancellationToken cancellationToken)
        {
            MachineToolNotificationController? previous = s_callback.Value;
            s_callback.Value = this;
            try
            {
                while (m_pendingIndex < m_pending.Count)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    (NodeState source, IFilterTarget value) = m_pending[m_pendingIndex];
                    await source.ReportEventAsync(m_context.Context, value, cancellationToken).ConfigureAwait(false);
                    m_pendingIndex++;
                }
                m_pending.Clear();
                m_pendingIndex = 0;
            }
            finally
            {
                s_callback.Value = previous;
            }
        }

        /// <summary>
        /// Stages all new identities before changing existing conditions, preserving rejected observations atomically.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="AggregateException"></exception>
        private async ValueTask ApplyAsync(
            MachineToolNotificationSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (Observation != null && snapshot.Revision < Observation.Revision)
            {
                throw Invalid("The notification provider returned a stale revision.");
            }
            var selected = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < snapshot.Alerts.Count; index++)
            {
                MachineToolAlert value = snapshot.Alerts[index];
                selected.Add(value.Key);
            }
            var removed = new List<AlertBinding>();
            foreach (AlertBinding binding in m_alerts.Values)
            {
                if (!selected.Contains(binding.Observation.Key))
                {
                    if (binding.Observation.IsActive ||
                        !binding.State.AckedState!.Id!.Value ||
                        binding.State.Retain!.Value)
                    {
                        throw Invalid("An active or unacknowledged alert cannot be removed.");
                    }
                    removed.Add(binding);
                }
            }
            bool published = ReferenceEquals(m_context.Manager.FindPredefinedNode(m_machine.NodeId), m_machine);
            var staged = new List<AlertBinding>();
            try
            {
                for (int index = 0; index < snapshot.Alerts.Count; index++)
                {
                    MachineToolAlert value = snapshot.Alerts[index];
                    if (!m_alerts.ContainsKey(value.Key))
                    {
                        var name = new QualifiedName(value.Key, m_context.InstanceNamespaceIndex);
                        AlertState node = m_options.CreateAlertAsync == null
                            ? m_context.Context.CreateInstanceOfAlertType(State, name)
                            : await m_options.CreateAlertAsync(m_context.Context, State, name, cancellationToken)
                                .ConfigureAwait(false);
                        ValidateFactoryNode(node);
                        var binding = new AlertBinding(node, value);
                        staged.Add(binding);
                        node.SourceNode!.Value = State.NodeId;
                        node.SourceName!.Value = m_machine.BrowseName.Name ?? string.Empty;
                        node.ConditionName!.Value = value.Key;
                        node.BranchId!.Value = NodeId.Null;
                        node.ClientUserId!.Value = string.Empty;
                        node.Comment!.Value = LocalizedText.Null;
                        node.AutoReportStateChanges = true;
                        node.SetEnableState(m_context.Context, true);
                        node.SetActiveState(m_context.Context, false);
                        node.SetAcknowledgedState(m_context.Context, !value.IsActive);
                        UpdateAlert(binding, value);
                        ConfigureConditionPermissions(binding);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (published)
                {
                    foreach (AlertBinding binding in staged)
                    {
                        await m_context.Manager.AddPredefinedNodeAsync(binding.State, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                foreach (AlertBinding binding in staged)
                {
                    try
                    {
                        await DeleteAsync(binding.State).ConfigureAwait(false);
                    }
                    catch (Exception cleanup)
                    {
                        errors.Add(cleanup);
                    }
                }
                if (errors.Count != 1)
                {
                    throw new AggregateException("Notification staging and rollback both failed.", errors);
                }
                throw;
            }
            lock (m_lifetimeGate)
            {
                foreach (AlertBinding binding in staged)
                {
                    m_alerts.Add(binding.Observation.Key, binding);
                    State.AddChild(binding.State);
                    State.AddReferenceIfMissing(ReferenceTypeIds.HasCondition, false, binding.State.NodeId);
                    binding.State.AddReferenceIfMissing(ReferenceTypeIds.HasCondition, true, State.NodeId);
                }
            }
            foreach (MachineToolAlert value in snapshot.Alerts)
            {
                AlertBinding binding = m_alerts[value.Key];
                bool changed = staged.Contains(binding) || binding.Observation != value;
                if (changed)
                {
                    if (!staged.Contains(binding))
                    {
                        UpdateAlert(binding, value);
                    }
                    if (binding.State.EnabledState!.Id!.Value)
                    {
                        var captured = new InstanceStateSnapshot();
                        captured.Initialize(m_context.Context, binding.State);
                        m_pending.Add((binding.State, captured));
                    }
                }
            }
            foreach (MachineToolMessage message in snapshot.Messages)
            {
                NotificationEventState value = m_context.Context.CreateInstanceOfNotificationEventType();
                value.TypeDefinitionId = m_messageType!.NodeId;
                value.EventType!.Value = m_messageType.NodeId;
                value.EventId!.Value = Uuid.NewUuid().ToByteString();
                value.Identifier!.Value = message.Identifier;
                value.SourceNode!.Value = State.NodeId;
                value.SourceName!.Value = m_machine.BrowseName.Name ?? string.Empty;
                value.Message!.Value = message.Message;
                value.Severity!.Value = message.Severity;
                value.Time!.Value = message.Time;
                value.ReceiveTime!.Value = m_timeProvider.GetUtcNow().UtcDateTime;
                m_pending.Add((State, value));
                m_sequence = message.Sequence;
            }
            Observation = snapshot;
            foreach (AlertBinding binding in removed)
            {
                lock (m_lifetimeGate)
                {
                    m_alerts.Remove(binding.Observation.Key);
                }
                State.RemoveChild(binding.State);
                State.RemoveReference(ReferenceTypeIds.HasCondition, false, binding.State.NodeId);
                await DeleteAsync(binding.State).ConfigureAwait(false);
            }
            await State.ClearChangeMasksAsync(m_context.Context, true, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Uses the standard alarm state transitions while preserving equipment quality and occurrence time.
        /// </summary>
        private void UpdateAlert(AlertBinding binding, MachineToolAlert value)
        {
            AlertState state = binding.State;
            bool transition = state.ActiveState!.Id!.Value != value.IsActive;
            if (value.IsActive && !state.ActiveState!.Id!.Value)
            {
                state.SetAcknowledgedState(m_context.Context, false);
            }
            if (transition)
            {
                state.SetActiveState(m_context.Context, value.IsActive);
                state.ActiveState.TransitionTime?.Value = value.Sample.SourceTimestamp;
            }
            state.SetSeverity(m_context.Context, (EventSeverity)value.Severity);
            state.ErrorCode!.Value = value.ErrorCode;
            state.Message!.Value = value.Message;
            NodeId classId = value.ConditionClassId.IsNull
                ? Ua.ObjectTypeIds.BaseConditionClassType : value.ConditionClassId;
            state.ConditionClassId!.Value = classId;
            state.ConditionClassName!.Value = m_context.Manager.FindPredefinedNode(classId)?.DisplayName ??
                new LocalizedText("BaseConditionClass");
            state.Quality!.Value = value.Sample.StatusCode;
            state.Quality.SourceTimestamp!.Value = value.Sample.SourceTimestamp;
            state.Quality.Timestamp = value.Sample.SourceTimestamp;
            state.Quality.StatusCode = value.Sample.StatusCode;
            state.EventId!.Value = Uuid.NewUuid().ToByteString();
            state.EventType!.Value = state.TypeDefinitionId;
            state.Time!.Value = value.Sample.SourceTimestamp;
            state.ReceiveTime!.Value = m_timeProvider.GetUtcNow().UtcDateTime;
            SetMetadata(state.ActiveState!, value.Sample);
            SetMetadata(state.ActiveState!.Id!, value.Sample);
            SetMetadata(state.ErrorCode, value.Sample);
            SetMetadata(state.Message, value.Sample);
            binding.Observation = value;
        }

        /// <summary>
        /// Rejects foreign, already-published or abstract factory results without taking ownership of borrowed nodes.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateFactoryNode(AlertState state)
        {
            if (state == null ||
                !ReferenceEquals(state.Parent, State) ||
                state.NodeId.IsNull ||
                state.NodeId.NamespaceIndex != m_context.InstanceNamespaceIndex ||
                m_context.Manager.FindPredefinedNode(state.NodeId) != null)
            {
                throw Invalid("The alert factory must return a fresh, unpublished child of this Messages node.");
            }
            if (m_context.Manager.FindPredefinedNode(state.TypeDefinitionId) is not BaseObjectTypeState type ||
                type.IsAbstract ||
                !m_context.Context.TypeTable.IsTypeOf(state.TypeDefinitionId,
                    ExpandedNodeId.ToNodeId(ObjectTypeIds.AlertType, m_context.Context.NamespaceUris)) ||
                state.ErrorCode == null ||
                state.ActiveState?.Id == null ||
                state.AckedState?.Id == null ||
                state.Enable == null ||
                state.Disable == null ||
                state.AddComment == null ||
                state.Acknowledge == null)
            {
                ReleaseReservations(state);
                throw Invalid("The factory must preserve a concrete generated AlertType and its mandatory nodes.");
            }
            if (ReferenceEquals(State.FindChild(m_context.Context, state.BrowseName), state))
            {
                State.RemoveChild(state);
                ReleaseReservations(state);
                throw Invalid("The alert factory must not attach or publish its staged node.");
            }
        }

        /// <summary>
        /// Applies provider permissions through core callbacks, preserving the standard condition lifecycle.
        /// </summary>
        private void ConfigureConditionPermissions(AlertBinding binding)
        {
            AlertState state = binding.State;
            ConditionEnableEventHandler? enable = state.OnEnableDisable;
            state.OnEnableDisable = (context, condition, enabled) =>
            {
                ServiceResult allowed = Permit(binding, enabled
                    ? MachineToolConditionOperations.Enable : MachineToolConditionOperations.Disable);
                return ServiceResult.IsBad(allowed) ? allowed : enable?.Invoke(context, condition, enabled) ??
                    ServiceResult.Good;
            };
            ConditionAddCommentEventHandler? comment = state.OnAddComment;
            state.OnAddComment = (context, condition, eventId, text) =>
            {
                ServiceResult allowed = Permit(binding, MachineToolConditionOperations.Comment);
                return ServiceResult.IsBad(allowed) ? allowed : comment?.Invoke(context, condition, eventId, text) ??
                    ServiceResult.Good;
            };
            ConditionAddCommentEventHandler? acknowledge = state.OnAcknowledge;
            state.OnAcknowledge = (context, condition, eventId, text) =>
            {
                ServiceResult allowed = Permit(binding, MachineToolConditionOperations.Acknowledge);
                return ServiceResult.IsBad(allowed) ? allowed :
                    acknowledge?.Invoke(context, condition, eventId, text) ?? ServiceResult.Good;
            };
        }

        /// <summary>
        /// Checks the latest committed permission policy, not a stale factory-time copy.
        /// </summary>
        private ServiceResult Permit(AlertBinding binding, MachineToolConditionOperations operation)
        {
            if (m_stopped)
            {
                return StatusCodes.BadShutdown;
            }
            return (binding.Observation.AllowedOperations & operation) != 0
                ? ServiceResult.Good : StatusCodes.BadUserAccessDenied;
        }

        /// <summary>
        /// Writes acquisition metadata without substituting a server-read timestamp.
        /// </summary>
        private static void SetMetadata(BaseVariableState state, MachineToolSampleInfo sample)
        {
            state.StatusCode = sample.StatusCode;
            state.Timestamp = sample.SourceTimestamp;
        }

        /// <summary>
        /// Adds discoverable notifier links while relying on the existing native Parent event route exactly once.
        /// </summary>
        private static void Link(BaseObjectState parent, BaseObjectState child)
        {
            parent.EventNotifier |= EventNotifiers.SubscribeToEvents;
            child.EventNotifier |= EventNotifiers.SubscribeToEvents;
            parent.AddReferenceIfMissing(ReferenceTypeIds.HasNotifier, false, child.NodeId);
            child.AddReferenceIfMissing(ReferenceTypeIds.HasNotifier, true, parent.NodeId);
        }

        /// <summary>
        /// Deletes only this controller's owned node and releases unpublished factory reservations.
        /// </summary>
        private async ValueTask DeleteAsync(NodeState node)
        {
            if (ReferenceEquals(m_context.Manager.FindPredefinedNode(node.NodeId), node))
            {
                await m_context.Manager.DeleteNodeAsync(m_context.Manager.SystemContext, node.NodeId,
                    CancellationToken.None).ConfigureAwait(false);
            }
            ReleaseReservations(node);
        }

        /// <summary>
        /// Releases all generated descendants from the shared instance-identity reservation owner.
        /// </summary>
        private void ReleaseReservations(NodeState node)
        {
            if (m_context.Context.NodeIdFactory is ICompanionNodeIdReservationOwner owner)
            {
                var children = new List<BaseInstanceState>();
                node.GetChildren(m_context.Context, children);
                foreach (BaseInstanceState child in children)
                {
                    ReleaseReservations(child);
                }
                owner.ReleaseNodeIdReservation(node);
            }
        }

        /// <summary>
        /// Creates a consistent protocol error for invalid observation state.
        /// </summary>
        private static ServiceResultException Invalid(string message)
        {
            return new(StatusCodes.BadInvalidState, message);
        }

        /// <summary>
        /// Couples a stable standard condition with its last accepted equipment observation.
        /// </summary>
        private sealed class AlertBinding(AlertState state, MachineToolAlert observation)
        {
            /// <summary>
            /// Gets the generated condition.
            /// </summary>
            public AlertState State { get; } = state;

            /// <summary>
            /// Gets or sets the committed provider data.
            /// </summary>
            public MachineToolAlert Observation { get; set; } = observation;
        }

        /// <summary>The one shared Machinery owner.</summary>
        private readonly IMachineryBuildContext m_context;

        /// <summary>The actual generated root and its native Parent route.</summary>
        private readonly MachineToolState m_machine;

        /// <summary>The explicit provider and limits.</summary>
        private readonly MachineToolNotificationOptions m_options;

        /// <summary>The injected publication clock.</summary>
        private readonly TimeProvider m_timeProvider;

        /// <summary>Serializes accepted inventories and standard client condition mutations.</summary>
        private readonly SemaphoreSlim m_update = new(1, 1);

        /// <summary>The per-instance stable condition identities.</summary>
        private readonly Dictionary<string, AlertBinding> m_alerts = new(StringComparer.Ordinal);

        /// <summary>Bounded snapshots awaiting publication, never an unbounded device queue.</summary>
        private readonly List<(NodeState Source, IFilterTarget Value)> m_pending = [];

        /// <summary>The application-owned concrete type for the abstract NotificationEventType.</summary>
        private BaseObjectTypeState? m_messageType;

        /// <summary>The last accepted informational notification sequence.</summary>
        private ulong m_sequence;

        /// <summary>The first pending event not yet successfully published.</summary>
        private int m_pendingIndex;

        /// <summary>Rejects recursive acquisition, mutation and self-disposal from callbacks.</summary>
        private static readonly AsyncLocal<MachineToolNotificationController?> s_callback = new();
    }
}
