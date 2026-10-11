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
using Opc.Ua.Vision.MachineVision;
using Control = Opc.Ua.Vision.MachineVision.VisionStateMachineTypeIds;

namespace Opc.Ua.Vision.Server
{
    public sealed partial class MachineVisionSystemHandle
    {
        /// <summary>
        /// Activates or resolves an application error condition. Resolving the final error
        /// returns an errored system to Preoperational; acknowledgement remains a separate operation.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public async ValueTask ReportErrorAsync(
            string key,
            LocalizedText message,
            bool active,
            bool persistent = false,
            ulong errorCode = 0,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("An error condition key is required.", nameof(key));
            }
            await EnterRuntimeAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                ConditionEntry entry = await GetConditionAsync(key, persistent, warning: false, cancellationToken)
                    .ConfigureAwait(false);
                entry.Active = active;
                entry.Condition.Message!.Value = message;
                entry.Condition.ErrorCode!.Value = errorCode;
                entry.Condition.ErrorString!.Value = message.Text ?? string.Empty;
                entry.Condition.Retain!.Value = active ||
                    !entry.Condition.AckedState!.Id!.Value ||
                    (entry.Condition.ConfirmedState != null && !entry.Condition.ConfirmedState.Id!.Value);
                if (active)
                {
                    entry.Condition.SetAcknowledgedState(m_context.Context, false);
                    if (entry.Condition.ConfirmedState != null)
                    {
                        entry.Condition.SetConfirmedState(m_context.Context, false);
                    }
                    await TransitionToErrorAsync(message.Text ?? key).ConfigureAwait(false);
                }
                else
                {
                    await RecoverResolvedErrorsAsync().ConfigureAwait(false);
                }
                await ReportConditionAsync(entry.Condition).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Publishes a non-blocking warning condition. Acknowledgement and optional
        /// confirmation retain the warning without interrupting productive operation.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public async ValueTask ReportWarningAsync(
            string key,
            LocalizedText message,
            bool active,
            bool requiresConfirmation = false,
            ulong errorCode = 0,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A warning condition key is required.", nameof(key));
            }
            await EnterRuntimeAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                ConditionEntry entry = await GetConditionAsync(key, requiresConfirmation, true, cancellationToken)
                    .ConfigureAwait(false);
                entry.Active = active;
                entry.Condition.Message!.Value = message;
                entry.Condition.ErrorString!.Value = message.Text ?? string.Empty;
                entry.Condition.ErrorCode!.Value = errorCode;
                if (active)
                {
                    entry.Condition.SetAcknowledgedState(m_context.Context, false);
                    if (entry.Condition.ConfirmedState != null)
                    {
                        entry.Condition.SetConfirmedState(m_context.Context, false);
                    }
                }
                entry.Condition.Retain!.Value = active ||
                    !entry.Condition.AckedState!.Id!.Value ||
                    entry.Condition.ConfirmedState?.Id?.Value == false;
                await ReportConditionAsync(entry.Condition).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Publishes an information or diagnostic event through the configured diagnostic threshold.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask ReportInformationAsync(
            LocalizedText message,
            uint diagnosticLevel = 0,
            CancellationToken cancellationToken = default)
        {
            await EnterRuntimeAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (diagnosticLevel == 0)
                {
                    await EmitAsync(new VisionInformationEventState(null), message.Text ?? string.Empty)
                        .ConfigureAwait(false);
                    return;
                }
                if (diagnosticLevel > 200)
                {
                    throw new ArgumentOutOfRangeException(nameof(diagnosticLevel));
                }
                if (!Has(MachineVisionFeatures.Diagnostics))
                {
                    throw ServiceResultException.Create(StatusCodes.BadNotSupported, "Diagnostics are not enabled.");
                }
                if (State.DiagnosticLevel!.Value >= diagnosticLevel)
                {
                    await EmitAsync(new VisionDiagnosticInfoEventState(null), message.Text ?? string.Empty,
                        notification => notification.Severity!.Value = (ushort)diagnosticLevel)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        private void Bind(
            NodeState parent,
            string name,
            Func<ISystemContext, ArrayOf<Variant>, List<Variant>, CancellationToken,
                ValueTask<MachineVisionOperationResult>> action)
        {
            MethodState method = MachineVisionNodes.Require<MethodState>(m_context.Manager, parent, name);
            method.Executable = true;
            method.UserExecutable = true;
            method.OnCallValidationFailedAsync = (_, called, _, arguments, _, ct) =>
                ReportInvalidCallAsync(called, arguments, ct);
            method.OnCallMethod2Async = async (context, called, _, arguments, output, ct) =>
            {
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, m_shutdown.Token);
                await EnterRuntimeAsync(lifetime.Token).ConfigureAwait(false);
                try
                {
                    ThrowIfDisposed();
                    MachineVisionOperationResult outcome = await action(context, arguments, output, lifetime.Token)
                        .ConfigureAwait(false);
                    if (outcome.ServiceResult == null || outcome.Error > 0)
                    {
                        throw new InvalidOperationException("The provider returned an invalid operation outcome.");
                    }
                    if (!outcome.Succeeded)
                    {
                        SetDomainError(called, output, outcome.Error);
                        await ReportMethodFailureAsync(called, arguments, ct).ConfigureAwait(false);
                    }
                    return outcome.ServiceResult;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (ServiceResultException exception)
                {
                    m_logger.OperationFailed(name, exception);
                    await ReportMethodFailureAsync(called, arguments, CancellationToken.None).ConfigureAwait(false);
                    return exception.Result;
                }
                catch (Exception exception)
                {
                    m_logger.OperationFailed(name, exception);
                    await ReportMethodFailureAsync(called, arguments, CancellationToken.None).ConfigureAwait(false);
                    return ServiceResult.Create(
                        exception, StatusCodes.BadInternalError, "The vision operation failed.");
                }
                finally
                {
                    m_gate.Release();
                }
            };
        }

        private async ValueTask ReportMethodFailureAsync(
            MethodState method, ArrayOf<Variant> arguments, CancellationToken ct)
        {
            string name = method.BrowseName.Name ?? string.Empty;
            ConditionEntry warning = await GetConditionAsync("Method:" + name, false, true, ct).ConfigureAwait(false);
            warning.Condition.SourceNode!.Value = method.NodeId;
            warning.Condition.SourceName!.Value = name;
            warning.Condition.ErrorString!.Value =
                $"Method {name} failed ({arguments.Count} input arguments; payloads are not logged).";
            warning.Condition.Message!.Value = new LocalizedText(warning.Condition.ErrorString.Value);
            warning.Condition.Retain!.Value = false;
            warning.Condition.SetAcknowledgedState(m_context.Context, true);
            await ReportConditionAsync(warning.Condition).ConfigureAwait(false);
        }

        private async ValueTask EnterErrorAsync(string reason, CancellationToken ct)
        {
            ConditionEntry error = await GetConditionAsync(reason, false, false, ct).ConfigureAwait(false);
            error.Active = true;
            error.Condition.ErrorString!.Value = reason;
            error.Condition.Message!.Value = new LocalizedText(reason);
            error.Condition.Retain!.Value = true;
            error.Condition.SetAcknowledgedState(m_context.Context, false);
            await ReportConditionAsync(error.Condition).ConfigureAwait(false);
            await TransitionToErrorAsync(reason).ConfigureAwait(false);
        }

        private async ValueTask TransitionToErrorAsync(string reason)
        {
            uint state = ControlState.GetStateId(ControlState.CurrentState!.Id!.Value);
            uint transition = state switch
            {
                Control.StateIds.Preoperational => Control.TransitionIds.PreoperationalToErrorAuto,
                Control.StateIds.Operational => Control.TransitionIds.OperationalToErrorAuto,
                _ => 0
            };
            if (transition != 0)
            {
                if (m_executionCancellation != null)
                {
                    await m_executionCancellation.CancelAsync().ConfigureAwait(false);
                }
                m_executionGeneration++;
                await TransitionAsync(ControlState, transition, reason).ConfigureAwait(false);
                AutomaticState.SetState(m_context.Context, 0);
                await RefreshStepsAsync().ConfigureAwait(false);
            }
        }

        private async ValueTask<ConditionEntry> GetConditionAsync(
            string key, bool persistent, bool warning, CancellationToken ct)
        {
            if (m_conditions.TryGetValue(key, out ConditionEntry? existing))
            {
                if (existing.Warning != warning || (persistent && existing.Condition.ConfirmedState == null))
                {
                    throw InvalidState("A condition key cannot change its warning or confirmation policy.");
                }
                return existing;
            }
            if (m_conditions.Count >= m_options.MaximumHandles)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadResourceUnavailable, "The vision condition limit has been reached.");
            }
            VisionConditionState condition = warning
                ? new VisionWarningConditionState(State)
                : persistent
                    ? new VisionPersistentErrorConditionState(State)
                    : new VisionErrorConditionState(State);
            if (persistent)
            {
                condition.CreateOrReplaceConfirmedState(m_context.Context, null);
                condition.CreateOrReplaceConfirm(m_context.Context, null);
            }
            var name = new QualifiedName("Condition:" + key, m_context.InstanceNamespaceIndex);
            condition.Create(m_context.Context, NodeId.Null, name, new LocalizedText(key), true);
            condition.CreateOrReplaceErrorString(m_context.Context, null);
            condition.CreateOrReplaceErrorCode(m_context.Context, null);
            condition.CreateOrReplaceStopReaction(m_context.Context, null).Value = !warning;
            condition.CreateOrReplaceBlockReaction(m_context.Context, null).Value = !warning;
            NodeId declaredType = condition.TypeDefinitionId;
            NodeState? declared = m_context.Manager.FindPredefinedNode<NodeState>(declaredType);
            if (declared is BaseObjectTypeState { IsAbstract: true })
            {
                var concreteId = new NodeId("MachineVisionCondition:" + declared.BrowseName.Name,
                    m_context.InstanceNamespaceIndex);
                if (m_context.Manager.FindPredefinedNode<NodeState>(concreteId) == null)
                {
                    var concrete = new BaseObjectTypeState
                    {
                        NodeId = concreteId,
                        BrowseName = new QualifiedName("Concrete" + declared.BrowseName.Name,
                            m_context.InstanceNamespaceIndex),
                        DisplayName = new LocalizedText("Concrete " + declared.BrowseName.Name),
                        SuperTypeId = declaredType,
                        IsAbstract = false
                    };
                    await m_context.Manager.AddPredefinedNodeAsync(concrete, ct).ConfigureAwait(false);
                }
                condition.TypeDefinitionId = concreteId;
            }
            condition.EventType!.Value = condition.TypeDefinitionId;
            condition.SourceNode!.Value = State.NodeId;
            condition.SourceName!.Value = State.BrowseName.Name ?? string.Empty;
            condition.ConditionName!.Value = key;
            condition.BranchId!.Value = NodeId.Null;
            condition.ClientUserId!.Value = string.Empty;
            condition.Quality!.Value = StatusCodes.Good;
            condition.Retain!.Value = false;
            condition.Comment!.Value = LocalizedText.Null;
            condition.SetEnableState(m_context.Context, true);
            condition.SetAcknowledgedState(m_context.Context, true);
            condition.SetSeverity(m_context.Context, warning ? EventSeverity.Medium :
                persistent ? EventSeverity.High : EventSeverity.MediumHigh);
            if (persistent)
            {
                condition.CreateOrReplaceConfirmedState(m_context.Context, null)
                    .CreateOrReplaceId(m_context.Context, null).Value = false;
            }
            condition.ReferenceTypeId = ReferenceTypeIds.HasCondition;
            MachineVisionNodes.Rebase(m_context.Context, condition);
            State.AddChild(condition);
            await m_context.Manager.AddPredefinedNodeAsync(condition, ct).ConfigureAwait(false);
            var entry = new ConditionEntry(condition, warning);
            condition.RetainStateProvider = _ => entry.Active;
            BindConditionMethod(entry, condition.Acknowledge!);
            if (condition.Confirm != null)
            {
                BindConditionMethod(entry, condition.Confirm);
            }
            m_conditions.Add(key, entry);
            return entry;
        }

        private void BindConditionMethod(ConditionEntry entry, AddCommentMethodState method)
        {
            var handler = method.OnCall ??
                throw new InvalidOperationException("The standard condition handler was not initialized.");
            Bind(entry.Condition, method.BrowseName.Name!, async (context, arguments, output, _) =>
            {
                if (!arguments[0].TryGetValue(out ByteString eventId) ||
                    !arguments[1].TryGetValue(out LocalizedText comment))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeMismatch, "An EventId and LocalizedText are required.");
                }
                ServiceResult result = handler(context, method, entry.Condition.NodeId, eventId, comment);
                if (ServiceResult.IsGood(result))
                {
                    await ReportConditionAsync(entry.Condition).ConfigureAwait(false);
                    await RecoverResolvedErrorsAsync().ConfigureAwait(false);
                }
                output.Clear();
                return new MachineVisionOperationResult(result);
            });
        }

        private async ValueTask<MachineVisionOperationResult> ConfirmAllAsync(
            ISystemContext context, ArrayOf<Variant> arguments, List<Variant> output, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!arguments[0].TryGetValue(out LocalizedText comment))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch, "A LocalizedText comment is required.");
            }
            foreach (ConditionEntry entry in m_conditions.Values)
            {
                if (NeedsConfirmation(entry) && !entry.Condition.AckedState!.Id!.Value)
                {
                    throw InvalidState("A condition must be acknowledged before confirmation.");
                }
            }
            foreach (ConditionEntry entry in m_conditions.Values)
            {
                if (NeedsConfirmation(entry))
                {
                    AddCommentMethodState method = entry.Condition.Confirm!;
                    ServiceResult confirmed = method.OnCall!(
                        context, method, entry.Condition.NodeId, entry.Condition.EventId!.Value, comment);
                    if (ServiceResult.IsBad(confirmed))
                    {
                        throw new ServiceResultException(confirmed);
                    }
                    await ReportConditionAsync(entry.Condition).ConfigureAwait(false);
                }
            }
            await RecoverResolvedErrorsAsync().ConfigureAwait(false);
            output.Clear();
            return MachineVisionOperationResult.Good;
        }

        private static bool NeedsConfirmation(ConditionEntry entry)
        {
            return entry.Condition.ConfirmedState?.Id?.Value == false &&
                (entry.Active || entry.Condition.Retain!.Value);
        }

        private bool HasActiveErrors()
        {
            if (m_statePersistenceFault)
            {
                return true;
            }
            foreach (ConditionEntry entry in m_conditions.Values)
            {
                if (!entry.Warning && (entry.Active || NeedsConfirmation(entry)))
                {
                    return true;
                }
            }
            return false;
        }

        private async ValueTask RecoverResolvedErrorsAsync()
        {
            if (HasActiveErrors())
            {
                return;
            }
            if (ControlState.GetStateId(ControlState.CurrentState!.Id!.Value) == Control.StateIds.Error)
            {
                await TransitionAsync(ControlState, Control.TransitionIds.ErrorToPreoperationalAuto)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask ReportConditionAsync(VisionConditionState condition)
        {
            condition.EventId!.Value = ByteString.From(Guid.NewGuid().ToByteArray());
            condition.Time!.Value = Now;
            condition.ReceiveTime!.Value = Now;
            await condition.ClearChangeMasksAsync(m_context.Context, true).ConfigureAwait(false);
            await condition.ReportEventAsync(m_context.Context, condition).ConfigureAwait(false);
        }

        private async ValueTask ReportInvalidCallAsync(
            MethodState method, ArrayOf<Variant> arguments, CancellationToken ct)
        {
            await EnterRuntimeAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await ReportMethodFailureAsync(method, arguments, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        private void ObserveMethodFailures(MethodState method, GenericMethodCalledEventHandler2Async action)
        {
            method.OnCallValidationFailedAsync = (_, called, _, arguments, _, ct) =>
                ReportInvalidCallAsync(called, arguments, ct);
            method.OnCallMethod2Async = async (context, called, objectId, arguments, output, ct) =>
            {
                try
                {
                    ServiceResult result = await action(context, called, objectId, arguments, output, ct)
                        .ConfigureAwait(false);
                    if (ServiceResult.IsBad(result))
                    {
                        await ReportInvalidCallAsync(called, arguments, ct).ConfigureAwait(false);
                    }
                    return result;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    m_logger.OperationFailed(called.BrowseName.Name ?? string.Empty, exception);
                    await ReportInvalidCallAsync(called, arguments, CancellationToken.None).ConfigureAwait(false);
                    return exception is ServiceResultException service
                        ? service.Result
                        : ServiceResult.Create(exception, StatusCodes.BadInternalError, "The vision operation failed.");
                }
            };
        }

        private ValueTask EmitAsync<T>(
            T notification, string message, Action<T>? configure = null, NodeState? source = null)
            where T : BaseEventState
        {
            notification.Create(
                m_context.Context, NodeId.Null, QualifiedName.Null, LocalizedText.Null, true);
            notification.Initialize(m_context.Context, source ?? State,
                notification is VisionInformationEventState ? EventSeverity.MediumLow : EventSeverity.Low,
                new LocalizedText(message));
            notification.Time!.Value = Now;
            notification.ReceiveTime!.Value = Now;
            configure?.Invoke(notification);
            return State.ReportEventAsync(m_context.Context, notification);
        }

        private static void SetDomainError(MethodState method, List<Variant> output, int error)
        {
            ArrayOf<Argument> arguments = default;
            if (method.OutputArguments is { } outputs)
            {
                arguments = outputs.Value;
            }
            for (int ii = 0; ii < arguments.Count && ii < output.Count; ii++)
            {
                if (string.Equals(arguments[ii].Name, "Error", StringComparison.OrdinalIgnoreCase))
                {
                    output[ii] = Variant.From(error);
                }
            }
        }

        private static void Outputs(List<Variant> output, params Variant[] values)
        {
            output.Clear();
            output.AddRange(values);
        }

        private static int Int32(ArrayOf<Variant> arguments, int index)
        {
            return arguments[index].TryGetValue(out int value) ? value
                : throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "An Int32 argument is required.");
        }

        private static uint UInt32(ArrayOf<Variant> arguments, int index)
        {
            return arguments[index].TryGetValue(out uint value) ? value
                : throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "A UInt32 argument is required.");
        }

        private static bool Boolean(ArrayOf<Variant> arguments, int index)
        {
            return arguments[index].TryGetValue(out bool value) ? value
                : throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "A Boolean argument is required.");
        }

        private static string Text(ArrayOf<Variant> arguments, int index)
        {
            return arguments[index].TryGetValue(out string value) ? value ?? string.Empty
                : throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "A String argument is required.");
        }

        private T Structure<T>(ArrayOf<Variant> arguments, int index) where T : class, IEncodeable, new()
        {
            return arguments[index].TryGetStructure<T>(m_context.Context.AsMessageContext(), out T? value) ? value
                : throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "The required structure is absent.");
        }

        private static ArrayOf<Variant> Variants(ArrayOf<Variant> arguments, int index)
        {
            return arguments[index].TryGetValue(out ArrayOf<Variant> values) ? values
                : throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "A Variant array is required.");
        }

        private readonly Dictionary<string, ConditionEntry> m_conditions = new(StringComparer.Ordinal);
        private bool m_statePersistenceFault;

        private sealed class ConditionEntry
        {
            public ConditionEntry(VisionConditionState condition, bool warning)
            {
                Condition = condition;
                Warning = warning;
            }

            public VisionConditionState Condition { get; }

            public bool Warning { get; }

            public bool Active { get; set; }
        }
    }
}
