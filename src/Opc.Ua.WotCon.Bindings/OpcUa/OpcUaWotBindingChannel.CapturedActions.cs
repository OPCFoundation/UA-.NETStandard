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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Bindings.OpcUa
{
    internal sealed partial class OpcUaWotBindingChannel
    {
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
            bool explicitReceiver = Form.Addressing.Metadata.TryGetValue("callObjectId", out string? receiverText);
            if ((!explicitReceiver && !Form.Addressing.Metadata.TryGetValue("componentOf", out receiverText)) ||
                string.IsNullOrEmpty(receiverText) ||
                !TryResolveNodeId(receiverText, out NodeId receiver, source.Context.NamespaceUris))
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid);
            }
            ResolvedPathTarget target = await ResolveTargetAsync(NodeClass.Method, cancellationToken)
                .ConfigureAwait(false);
            source.Validate();
            NodeId method = target.NodeId;
            return new WotCapturedConditionAction(source, receiver, method,
                (request, token) => InvokeCapturedConditionAsync(source, receiver, method, request, token));
        }

        private async ValueTask<WotInvokeResult> InvokeCapturedConditionAsync(
            WotEventSource source,
            NodeId receiver,
            NodeId method,
            WotInvokeRequest request,
            CancellationToken cancellationToken)
        {
            source.Validate();
            ArrayOf<Variant> inputs = Form.ConditionInvocation is { } invocation
                ? invocation.NormalizeInputs(request.Inputs) : request.Inputs;
            Form.Payload.ValidateInputs(inputs, request.Context);
            inputs = inputs.ConvertAll(value => WotBindingValueMapper.Translate(
                value, request.Context, source.Context));
            ArrayOf<CallMethodRequest> requests =
            [
                new CallMethodRequest { ObjectId = receiver, MethodId = method, InputArguments = inputs }
            ];
            RequestHeader? header = request.DiagnosticsMask == DiagnosticsMasks.None
                ? null : new RequestHeader { ReturnDiagnostics = (uint)request.DiagnosticsMask };
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
            Form.Payload.ValidateOutputs(result.OutputArguments, source.Context);
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
