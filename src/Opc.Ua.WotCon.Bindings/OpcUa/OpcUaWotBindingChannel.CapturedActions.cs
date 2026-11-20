// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings.OpcUa
{
    /// <summary>
    /// A live OPC UA binding channel that translates WoT operations onto OPC UA
    /// services: read / write of a NodeId Value attribute, observe and event
    /// subscription via a native <c>Subscription</c> / <c>MonitoredItem</c>
    /// pair (Part 4 §5.12 / §5.13), and action invocation via Method Call
    /// preserving argument order and <see cref="DataValue"/> / <see cref="StatusCode"/>
    /// metadata.
    /// </summary>
    internal sealed partial class OpcUaWotBindingChannel
    {
        /// <summary>
        /// Captures the condition Method source used for captured condition actions.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public async ValueTask<WotCapturedConditionAction> CaptureConditionActionAsync(
            CancellationToken cancellationToken = default)
        {
            WotEventSource source = m_eventSource ??
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The channel cannot retain an authenticated action binding.");
            source.Validate();
            if (!source.IsAuthenticated || Form.Operation != WoTBindingCapabilityEnum.InvokeAction)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "A Condition action requires an authenticated action source.");
            }
            ResolvedPathTarget target = await ResolveTargetAsync(NodeClass.Method, cancellationToken)
                .ConfigureAwait(false);
            NodeId receiver = await ResolveCallReceiverAsync(target, cancellationToken).ConfigureAwait(false);
            _ = await ResolveNativeMethodPayloadAsync(target, cancellationToken).ConfigureAwait(false);
            source.Validate();
            NodeId method = target.NodeId;
            return new WotCapturedConditionAction(source, receiver, method,
                (request, token) => InvokeCapturedConditionAsync(source, receiver, target, request, token));
        }

        private async ValueTask<WotInvokeResult> InvokeCapturedConditionAsync(
            WotEventSource source,
            NodeId receiver,
            ResolvedPathTarget target,
            WotInvokeRequest request,
            CancellationToken cancellationToken)
        {
            source.Validate();
            await ValidateSourceCallReceiverAsync(receiver, target, cancellationToken).ConfigureAwait(false);
            WotPayloadDescriptor payload = await ResolveNativeMethodPayloadAsync(
                target, cancellationToken).ConfigureAwait(false);
            ArrayOf<Variant> inputs = Form.ConditionInvocation is { } invocation
                ? invocation.NormalizeInputs(request.Inputs) : request.Inputs;
            payload.ValidateInputs(inputs, request.Context);
            inputs = inputs.ConvertAll(value => WotBindingValueMapper.Translate(
                value, request.Context, source.Context));
            ArrayOf<CallMethodRequest> requests =
            [
                new CallMethodRequest { ObjectId = receiver, MethodId = target.NodeId, InputArguments = inputs }
            ];
            RequestHeader? header = request.DiagnosticsMask == DiagnosticsMasks.None
                ? null : new RequestHeader { ReturnDiagnostics = (uint)request.DiagnosticsMask };
            ValidatePathSessionState(target.State);
            CallResponse response = await source.Client.CallAsync(header, requests, cancellationToken)
                .ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
            source.Validate();
            CallMethodResult result = response.Results[0];
            ValidateInvocationDetails(result, inputs.Count, response);
            ServiceResult operation = ClientBase.GetResult(
                result.StatusCode, 0, response.DiagnosticInfos, response.ResponseHeader);
            ArrayOf<StatusCode> inputStatuses = result.InputArgumentResults;
            ClientBase.ValidateDiagnosticInfos(result.InputArgumentDiagnosticInfos, inputStatuses);
            var inputResults = new ServiceResult[inputStatuses.Count];
            for (int index = 0; index < inputResults.Length; index++)
            {
                inputResults[index] = ClientBase.GetResult(
                    inputStatuses[index], index, result.InputArgumentDiagnosticInfos, response.ResponseHeader);
            }
            if (StatusCode.IsBad(result.StatusCode))
            {
                return new WotInvokeResult(result.StatusCode, error: operation.ToString())
                    .WithContext(source.Context).WithResultDetails(operation, inputResults);
            }
            payload.ValidateOutputs(result.OutputArguments, source.Context);
            var outputs = new DataValue[result.OutputArguments.Count];
            DateTimeUtc now = DateTimeUtc.Now;
            for (int index = 0; index < outputs.Length; index++)
            {
                outputs[index] = new DataValue(result.OutputArguments[index], StatusCodes.Good, now, now);
            }
            return new WotInvokeResult(result.StatusCode, outputs)
                .WithContext(source.Context).WithResultDetails(operation, inputResults);
        }
    }
}
