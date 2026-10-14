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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Programs;

namespace Opc.Ua.Server.Programs
{
    public sealed partial class ProgramRuntime
    {
        private void InitializeDiagnostics()
        {
            m_diagnostics.InvocationCreationTime = m_timeProvider.GetUtcNow().UtcDateTime;
            if (!Definition.DiagnosticsEnabled)
            {
                return;
            }
            if (Node.ProgramDiagnostic is null)
            {
                ProgramDiagnostic2State diagnostic = Node.CreateOrReplaceProgramDiagnostic(m_context, null, false);
                diagnostic.Create(m_context, diagnostic.NodeId,
                    new QualifiedName(BrowseNames.ProgramDiagnostic),
                    new LocalizedText(BrowseNames.ProgramDiagnostic), false);
            }
            if (Node.ProgramDiagnostic is null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "The Program model does not provide ProgramDiagnostic2.");
            }
            ProtectDiagnostics(Node.ProgramDiagnostic);
            PublishDiagnostics();
        }

        private void ProtectDiagnostics(NodeState node)
        {
            if (node is BaseVariableState variable)
            {
                variable.AccessLevel = AccessLevels.CurrentRead;
                variable.UserAccessLevel = AccessLevels.CurrentRead;
                variable.OnSimpleReadValue = ReadDiagnosticValue;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_context, children);
            foreach (BaseInstanceState child in children)
            {
                ProtectDiagnostics(child);
            }
        }

        private ServiceResult ReadDiagnosticValue(ISystemContext context, NodeState node, ref Variant value)
        {
            if (context is not ISessionSystemContext session || session.SessionId is not { } sessionId ||
                sessionId.IsNull)
            {
                return ServiceResult.Good;
            }
            bool encrypted = context is ServerSystemContext serverContext &&
                serverContext.OperationContext?.ChannelContext?.EndpointDescription?.SecurityMode ==
                    MessageSecurityMode.SignAndEncrypt;
            bool authorized = sessionId == m_diagnostics.CreateSessionId ||
                session.UserIdentity?.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_SecurityAdmin) == true;
            if (!encrypted || !authorized)
            {
                value = Variant.Null;
                return StatusCodes.BadUserAccessDenied;
            }
            return ServiceResult.Good;
        }

        private void PublishDiagnostics()
        {
            if (!Definition.DiagnosticsEnabled || Node.ProgramDiagnostic is not { } diagnostic)
            {
                return;
            }
            diagnostic.Value = Required(CoreUtils.Clone(m_diagnostics));
            Required(diagnostic.CreateSessionId).Value = m_diagnostics.CreateSessionId;
            Required(diagnostic.CreateClientName).Value = m_diagnostics.CreateClientName ?? string.Empty;
            Required(diagnostic.InvocationCreationTime).Value = m_diagnostics.InvocationCreationTime;
            Required(diagnostic.LastTransitionTime).Value = m_diagnostics.LastTransitionTime;
            Required(diagnostic.LastMethodCall).Value = m_diagnostics.LastMethodCall ?? string.Empty;
            Required(diagnostic.LastMethodSessionId).Value = m_diagnostics.LastMethodSessionId;
            Required(diagnostic.LastMethodInputArguments).Value =
                CoreUtils.Clone(m_diagnostics.LastMethodInputArguments);
            Required(diagnostic.LastMethodOutputArguments).Value =
                CoreUtils.Clone(m_diagnostics.LastMethodOutputArguments);
            Required(diagnostic.LastMethodInputValues).Value = CoreUtils.Clone(m_diagnostics.LastMethodInputValues);
            Required(diagnostic.LastMethodOutputValues).Value = CoreUtils.Clone(m_diagnostics.LastMethodOutputValues);
            Required(diagnostic.LastMethodCallTime).Value = m_diagnostics.LastMethodCallTime;
            Required(diagnostic.LastMethodReturnStatus).Value = m_diagnostics.LastMethodReturnStatus;
            diagnostic.ClearChangeMasks(m_context, true);
        }

        private async ValueTask RecordRejectedCallAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            ServiceResult error,
            CancellationToken cancellationToken)
        {
            if (!BeginOperation())
            {
                return;
            }
            try
            {
                await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    RecordCall(context, method, inputs, default, error);
                }
                finally
                {
                    m_gate.Release();
                }
            }
            finally
            {
                EndOperation();
            }
        }

        private void RecordCall(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputs,
            ArrayOf<Variant> outputs,
            ServiceResult result)
        {
            m_diagnostics.LastMethodCall = method.BrowseName.Name ?? string.Empty;
            m_diagnostics.LastMethodSessionId = GetSessionId(context);
            m_diagnostics.LastMethodCallTime = m_timeProvider.GetUtcNow().UtcDateTime;
            m_diagnostics.LastMethodReturnStatus = result.StatusCode;
            m_diagnostics.LastMethodInputArguments = CoreUtils.Clone(method.InputArguments?.Value ?? default);
            m_diagnostics.LastMethodOutputArguments = CoreUtils.Clone(method.OutputArguments?.Value ?? default);
            m_diagnostics.LastMethodInputValues = AlignValues(inputs, m_diagnostics.LastMethodInputArguments.Count);
            m_diagnostics.LastMethodOutputValues = AlignValues(outputs, m_diagnostics.LastMethodOutputArguments.Count);
            PublishDiagnostics();
            if (context is ServerSystemContext serverContext)
            {
                serverContext.Server.ReportAuditUpdateMethodEvent(context, Node.NodeId, method.NodeId,
                    inputs, $"Program {method.BrowseName.Name}", result.StatusCode, m_logger);
            }
        }

        private static ArrayOf<Variant> AlignValues(ArrayOf<Variant> values, int length)
        {
            var aligned = new Variant[length];
            for (int index = 0; index < Math.Min(length, values.Count); index++)
            {
                aligned[index] = CoreUtils.Clone(values[index]);
            }
            return aligned;
        }

        private async ValueTask<ServiceResult> CommitAsync(
            ISystemContext context,
            FiniteStateMachineState machine,
            PreparedStateMachineTransition prepared,
            Variant intermediateResult,
            ArrayOf<ProgramResultValue> finalResults,
            ProgramControlMethod? control,
            MethodState? method,
            ArrayOf<Variant> inputs,
            Action? committed)
        {
            FiniteStateVariableState oldState = Required(CoreUtils.Clone(machine.CurrentState));
            DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
            ProgramTransitionEventState transitionEvent =
                Required(Definition.TransitionEventFactory(machine.GetTransitionNodeId(prepared.TransitionId)));
            transitionEvent.Create(context, NodeId.Null, QualifiedName.Null, LocalizedText.Null, false);
            transitionEvent.Initialize(context, Node, EventSeverity.Medium,
                new LocalizedText("Program state transition."));
            BaseDataVariableState intermediate = Required(transitionEvent.IntermediateResult);
            if (TypeInfo.IsInstanceOfDataType(intermediateResult, intermediate.DataType, intermediate.ValueRank,
                context.NamespaceUris, context.TypeTable).IsUnknown)
            {
                return ServiceResult.Create(StatusCodes.BadTypeMismatch,
                    "IntermediateResult does not match the selected Program event model.");
            }
            AuditProgramTransitionEventState? auditEvent = null;
            if (context is ServerSystemContext { Server.Auditing: true })
            {
                auditEvent = Required(Definition.AuditEventFactory(
                    machine.GetTransitionNodeId(prepared.TransitionId)));
                auditEvent.Create(context, NodeId.Null, QualifiedName.Null, LocalizedText.Null, false);
                auditEvent.Initialize(context, Node, EventSeverity.Medium,
                    new LocalizedText("Program state transition."), control.HasValue, now);
            }
            ServiceResult result = machine.CommitTransition(context, prepared, () =>
            {
                committed?.Invoke();
                ApplyFinalResults(finalResults, now);
                machine.LastTransition!.TransitionTime!.Value = now;
                machine.LastTransition.StatusCode = StatusCodes.Good;
                Required(machine.CurrentState).Timestamp = now;
                machine.LastTransition.Timestamp = now;
                if (ReferenceEquals(machine, Node))
                {
                    Volatile.Write(ref m_currentState, (int)Required(Required(machine.CurrentState).Number).Value);
                }
                m_diagnostics.LastTransitionTime = now;
                PublishDiagnostics();
                transitionEvent.SetChildValue(context, BrowseNames.FromState, oldState, false);
                transitionEvent.SetChildValue(context, BrowseNames.ToState,
                    CoreUtils.Clone(machine.CurrentState), false);
                transitionEvent.SetChildValue(context, BrowseNames.Transition,
                    CoreUtils.Clone(machine.LastTransition), false);
                Required(transitionEvent.IntermediateResult).Value = CoreUtils.Clone(intermediateResult);
                Required(transitionEvent.Time).Value = now;
                if (auditEvent is not null)
                {
                    Required(auditEvent.SourceName).Value = control.HasValue ? $"Method/{control}" : "Internal";
                    Required(auditEvent.OldStateId).Value = Required(oldState.Id).Value;
                    Required(auditEvent.NewStateId).Value = Required(Required(machine.CurrentState).Id).Value;
                    Required(auditEvent.TransitionNumber).Value = Required(machine.LastTransition.Number).Value;
                    if (method is not null)
                    {
                        Required(auditEvent.MethodId).Value = method.NodeId;
                        Required(auditEvent.InputArguments).Value = CoreUtils.Clone(inputs);
                    }
                }
            });
            if (ServiceResult.IsGood(result))
            {
                Node.ClearChangeMasks(context, true);
                try
                {
                    await Node.ReportEventAsync(context, transitionEvent, m_lifetime.Token).ConfigureAwait(false);
                }
                finally
                {
                    if (auditEvent is not null && context is ServerSystemContext serverContext)
                    {
                        serverContext.Server.ReportAuditEvent(context, auditEvent);
                    }
                }
            }
            return result;
        }

        private void ValidateFinalResults(ArrayOf<ProgramResultValue> results)
        {
            foreach (ProgramResultValue result in results)
            {
                if (result.Path.Count == 0 || Node.FinalResultData is null ||
                    Node.FinalResultData.FindChild(m_context, result.Path, 0) is not BaseVariableState variable)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                        "A final result must identify a declared Variable beneath FinalResultData.");
                }
                if (TypeInfo.IsInstanceOfDataType(result.Value.WrappedValue, variable.DataType,
                    variable.ValueRank, m_context.NamespaceUris, m_context.TypeTable).IsUnknown)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "A final result does not match its declared data type.");
                }
            }
        }

        private void ApplyFinalResults(ArrayOf<ProgramResultValue> results, DateTime timestamp)
        {
            foreach (ProgramResultValue result in results)
            {
                var variable = (BaseVariableState)Required(
                    Required(Node.FinalResultData).FindChild(m_context, result.Path, 0));
                variable.Value = CoreUtils.Clone(result.Value.WrappedValue);
                variable.StatusCode = result.Value.StatusCode;
                variable.Timestamp = timestamp;
            }
        }

        private void ClearFinalResults()
        {
            if (Node.FinalResultData is not null)
            {
                ClearFinalResultNode(Node.FinalResultData);
            }
        }

        private void ClearFinalResultNode(NodeState parent)
        {
            if (parent is BaseVariableState variable)
            {
                variable.Value = Variant.Null;
                variable.StatusCode = StatusCodes.BadWaitingForInitialData;
                variable.Timestamp = m_timeProvider.GetUtcNow().UtcDateTime;
            }
            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_context, children);
            foreach (BaseInstanceState child in children)
            {
                ClearFinalResultNode(child);
            }
        }

        private readonly ProgramDiagnostic2DataType m_diagnostics = new() { LastMethodCall = string.Empty };
    }
}
