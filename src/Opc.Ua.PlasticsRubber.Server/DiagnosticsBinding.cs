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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Describes one completed device-specific diagnosis step.
    /// </summary>
    /// <param name="InputNode">The actual component or interface root diagnosed.</param>
    /// <param name="Succeeded">Whether the step completed without an error.</param>
    /// <param name="Message">The device's localized result description.</param>
    public sealed record DiagnosticsStep(NodeId InputNode, bool Succeeded, LocalizedText Message);

    /// <summary>
    /// Carries the absolute diagnostics status and newly completed steps for one authoritative revision.
    /// </summary>
    /// <param name="Revision">The monotonically increasing authority revision.</param>
    /// <param name="Status">The actual common diagnostics state.</param>
    /// <param name="SourceTimestamp">The time the authority committed the result.</param>
    /// <param name="Steps">The bounded set of steps completed at this revision.</param>
    /// <param name="Message">The localized summary used for a completion event.</param>
    public sealed record DiagnosticsSnapshot(
        ulong Revision,
        DiagnosticsStatusEnumeration Status,
        DateTimeUtc SourceTimestamp,
        ArrayOf<DiagnosticsStep> Steps,
        LocalizedText Message);

    /// <summary>
    /// Starts and stops actual device diagnostics without defining their vendor-specific algorithms.
    /// </summary>
    /// <remarks>
    /// Run returns an active status even when diagnostics are already active. Completion is
    /// published through DiagnosticsBinding.PublishAsync. Stop cancels active diagnostics and
    /// returns OFF. Providers return snapshots rather than reentering publication during these calls.
    /// </remarks>
    public interface IDiagnosticsProvider
    {
        /// <summary>
        /// Commits the start request and returns its active status.
        /// </summary>
        ValueTask<DiagnosticsSnapshot> RunAsync(
            PlasticsRubberRequestContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits cancellation and returns OFF, including when diagnostics had already completed.
        /// </summary>
        ValueTask<DiagnosticsSnapshot> StopAsync(
            PlasticsRubberRequestContext context,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implements diagnostics method semantics and generated result events on a real device interface.
    /// </summary>
    public sealed class DiagnosticsBinding : IAsyncDisposable
    {
        /// <summary>
        /// Captures the existing owner and concrete interface source.
        /// </summary>
        private DiagnosticsBinding(
            AsyncCustomNodeManager owner,
            DiagnosticsState node,
            BaseObjectState source,
            IPlasticsRubberAccessPolicy policy)
        {
            m_owner = owner;
            Node = node;
            m_source = source;
            m_binding = new PlasticsRubberMethodBinding(node, owner.SystemContext, policy);
            m_eventCleanup = new Lazy<Task>(DeleteEventTypesAsync);
        }

        /// <summary>
        /// Gets the generated diagnostics instance.
        /// </summary>
        public DiagnosticsState Node { get; }

        /// <summary>
        /// Creates concrete event subtypes and binds the mandatory generated diagnostic methods.
        /// </summary>
        public static async ValueTask<DiagnosticsBinding> CreateAsync(
            AsyncCustomNodeManager owner,
            DiagnosticsState node,
            BaseObjectState source,
            IDiagnosticsProvider provider,
            IPlasticsRubberAccessPolicy policy,
            CancellationToken cancellationToken = default)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }
            if (node.RunDiagnostics == null || node.StopDiagnostics == null || node.Status == null)
            {
                throw new ArgumentException("Mandatory diagnostics members are missing.", nameof(node));
            }
            var binding = new DiagnosticsBinding(owner, node, source, policy);
            try
            {
                binding.m_stepType = await binding.CreateEventTypeAsync(
                    ObjectTypeIds.DiagnosisStepEndEventType, "Step", cancellationToken).ConfigureAwait(false);
                binding.m_endType = await binding.CreateEventTypeAsync(
                    ObjectTypeIds.DiagnosisEndEventType, "End", cancellationToken).ConfigureAwait(false);
                source.EventNotifier |= EventNotifiers.SubscribeToEvents;
                owner.AddRootNotifier(source);
                binding.m_binding.Bind(node.RunDiagnostics, async (request, ct) =>
                {
                    DiagnosticsSnapshot snapshot = await provider.RunAsync(request, ct).ConfigureAwait(false);
                    if (snapshot.Status is not (DiagnosticsStatusEnumeration.ACTIVE_OK or
                        DiagnosticsStatusEnumeration.ACTIVE_ERROR_DETECTED))
                    {
                        throw Error("RunDiagnostics must enter or retain an active state.");
                    }
                    await binding.ApplyAsync(snapshot, ct).ConfigureAwait(false);
                });
                binding.m_binding.Bind(node.StopDiagnostics, async (request, ct) =>
                {
                    DiagnosticsSnapshot snapshot = await provider.StopAsync(request, ct).ConfigureAwait(false);
                    if (snapshot.Status != DiagnosticsStatusEnumeration.OFF)
                    {
                        throw Error("StopDiagnostics must leave diagnostics OFF.");
                    }
                    await binding.ApplyAsync(snapshot, ct).ConfigureAwait(false);
                });
                return binding;
            }
            catch
            {
                await binding.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Publishes committed diagnostics, including the required end event on completion.
        /// </summary>
        public async ValueTask PublishAsync(
            DiagnosticsSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            DiagnosticsSnapshot frozen = snapshot with { Steps = snapshot.Steps.ToArray().ToArrayOf() };
            ServiceResult result = await m_binding.RunAsync(
                ct => ApplyAsync(frozen, ct), cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await m_binding.DisposeAsync().ConfigureAwait(false);
            await m_eventCleanup.Value.ConfigureAwait(false);
        }

        /// <summary>
        /// Removes owned event declarations once after all diagnostic operations have drained.
        /// </summary>
        private async Task DeleteEventTypesAsync()
        {
            List<Exception>? failures = null;
            foreach (NodeId typeId in m_eventTypes)
            {
                try
                {
                    await m_owner.DeleteNodeAsync(m_owner.SystemContext, typeId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }
            if (failures != null)
            {
                throw new AggregateException("Diagnostics event cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Applies one complete revision without duplicating the events of a successful replay.
        /// </summary>
        private async ValueTask ApplyAsync(DiagnosticsSnapshot snapshot, CancellationToken ct)
        {
            if (snapshot.SourceTimestamp == DateTimeUtc.MinValue || snapshot.Steps.Count > 256 ||
                (int)snapshot.Status < 0 || (int)snapshot.Status > 4)
            {
                throw Error("Diagnostics require a valid state, source time and at most 256 completed steps.");
            }
            foreach (DiagnosticsStep step in snapshot.Steps)
            {
                if (step == null || step.InputNode.IsNull || step.Message.IsNull)
                {
                    throw Error("A completed step requires a component and a result description.");
                }
                if (!step.Succeeded && snapshot.Status is
                    DiagnosticsStatusEnumeration.ACTIVE_OK or DiagnosticsStatusEnumeration.COMPLETE)
                {
                    throw Error("The diagnostics state may not hide a failed step.");
                }
            }
            if (m_last != null && snapshot.Revision <= m_last.Revision)
            {
                if (snapshot == m_last || (snapshot.Revision == m_last.Revision &&
                    snapshot.Status == m_last.Status && snapshot.SourceTimestamp == m_last.SourceTimestamp &&
                    snapshot.Message == m_last.Message && snapshot.Steps == m_last.Steps))
                {
                    return;
                }
                throw Error("The diagnostics revision is stale or conflicting.");
            }
            for (int index = 0; index < snapshot.Steps.Count; index++)
            {
                DiagnosticsStep step = snapshot.Steps[index];
                var notification = new DiagnosisStepEndEventState(null);
                Initialize(notification, m_stepType, snapshot.SourceTimestamp, step.Message,
                    step.Succeeded ? EventSeverity.Low : EventSeverity.High);
                notification.InputNode!.Value = step.InputNode;
                notification.Result!.Value = step.Succeeded;
                await m_source.ReportEventAsync(m_owner.SystemContext, notification, ct).ConfigureAwait(false);
            }
            Node.Status!.Value = snapshot.Status;
            Node.Status.StatusCode = StatusCodes.Good;
            Node.Status.Timestamp = snapshot.SourceTimestamp;
            Node.ClearChangeMasks(m_owner.SystemContext, true);
            if (snapshot.Status is DiagnosticsStatusEnumeration.COMPLETE or
                DiagnosticsStatusEnumeration.COMPLETE_ERROR_DETECTED)
            {
                var notification = new DiagnosisEndEventState(null);
                Initialize(notification, m_endType, snapshot.SourceTimestamp, snapshot.Message,
                    snapshot.Status == DiagnosticsStatusEnumeration.COMPLETE ? EventSeverity.Low : EventSeverity.High);
                notification.AddStatus(m_owner.SystemContext);
                notification.Status!.Value = snapshot.Status;
                await m_source.ReportEventAsync(m_owner.SystemContext, notification, ct).ConfigureAwait(false);
            }
            m_last = snapshot with { Steps = snapshot.Steps.ToArray().ToArrayOf() };
        }

        /// <summary>
        /// Creates application-scoped concrete derivatives of the abstract common diagnostic events.
        /// </summary>
        private async ValueTask<NodeId> CreateEventTypeAsync(
            ExpandedNodeId standardType,
            string suffix,
            CancellationToken ct)
        {
            var type = new BaseObjectTypeState
            {
                NodeId = new NodeId(
                    Node.NodeId.WithNamespaceIndex(0).ToString() + "/Diagnostics/" + suffix,
                    Node.NodeId.NamespaceIndex),
                BrowseName = new QualifiedName("Diagnostics" + suffix + "EventType", Node.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText("Diagnostics " + suffix),
                SuperTypeId = ExpandedNodeId.ToNodeId(standardType, m_owner.SystemContext.NamespaceUris),
                IsAbstract = false,
                IsPartOfTypeHierarchy = true
            };
            if (m_owner.FindPredefinedNode<NodeState>(type.NodeId) != null)
            {
                throw Error("The concrete diagnostics event identity is already owned.");
            }
            m_eventTypes.Add(type.NodeId);
            await m_owner.AddPredefinedNodeAsync(type, ct).ConfigureAwait(false);
            return type.NodeId;
        }

        /// <summary>
        /// Initializes a generated event with an actual concrete type and original source time.
        /// </summary>
        private void Initialize(
            BaseEventState notification,
            NodeId type,
            DateTimeUtc time,
            LocalizedText message,
            EventSeverity severity)
        {
            notification.Create(m_owner.SystemContext, NodeId.Null, QualifiedName.Null, LocalizedText.Null, true);
            notification.Initialize(m_owner.SystemContext, m_source, severity, message);
            notification.TypeDefinitionId = type;
            notification.EventType!.Value = type;
            notification.Time!.Value = time;
        }

        /// <summary>
        /// Creates a precise invalid-state failure for contradictory diagnostics.
        /// </summary>
        private static ServiceResultException Error(string message)
        {
            return ServiceResultException.Create(StatusCodes.BadInvalidState, message);
        }

        /// <summary>
        /// The actual model and instance owner.
        /// </summary>
        private readonly AsyncCustomNodeManager m_owner;

        /// <summary>
        /// The device interface specified as the source of diagnostics events.
        /// </summary>
        private readonly BaseObjectState m_source;

        /// <summary>
        /// The common authorization and lifetime guard.
        /// </summary>
        private readonly PlasticsRubberMethodBinding m_binding;

        /// <summary>
        /// The event type declarations owned by this binding.
        /// </summary>
        private readonly List<NodeId> m_eventTypes = [];

        /// <summary>
        /// The concrete step event subtype.
        /// </summary>
        private NodeId m_stepType;

        /// <summary>
        /// The concrete completion event subtype.
        /// </summary>
        private NodeId m_endType;

        /// <summary>
        /// The last completely published revision.
        /// </summary>
        private DiagnosticsSnapshot? m_last;

        /// <summary>
        /// Shares terminal event cleanup between all concurrent disposers.
        /// </summary>
        private readonly Lazy<Task> m_eventCleanup;
    }
}
