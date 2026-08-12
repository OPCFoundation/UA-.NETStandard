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
using Opc.Ua.AI.Inference;
using Opc.Ua;
using Opc.Ua.AI;
using AIRefs = Opc.Ua.AI.ReferenceTypeIds;
using BrowseNames = Opc.Ua.AI.BrowseNames;
using ObjectIds = Opc.Ua.ObjectIds;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;

namespace Opc.Ua.AI.Server
{
    public sealed partial class AINodeManager
    {
        /// <summary>
        /// Attaches handlers to the methods a deployment offers.
        /// </summary>
        /// <remarks>
        /// Only the asynchronous handlers are attached. Inference is a network call
        /// on any deployment worth having, and blocking a Server thread on one would
        /// make throughput a function of model latency.
        /// </remarks>
        private void WireDeploymentMethods(DeploymentState deployment)
        {
            Child<InvokeMethodState>(deployment, BrowseNames.Invoke).OnCallAsync =
                (context, method, objectId, payload, payloadUri, contentType, parameters, timeout, ct) =>
                    InvokeAsync(objectId, payload, payloadUri, contentType, timeout, ct);

            Child<InvokeAsyncMethodState>(deployment, BrowseNames.InvokeAsync).OnCallAsync =
                (context, method, objectId, payload, payloadUri, contentType, parameters, ct) =>
                    StartJobAsync(objectId, payload, payloadUri, contentType, ct);

            Child<GetCapabilitiesMethodState>(deployment, BrowseNames.GetCapabilities)
                .OnCallAsync =
                (context, method, objectId, ct) => GetCapabilitiesAsync(objectId, ct);

            Child<BeginTransferMethodState>(deployment, BrowseNames.BeginTransfer).OnCallAsync =
                (context, method, objectId, contentType, requestSize, ct) =>
                    BeginTransferAsync(objectId, contentType, requestSize, ct);
        }

        /// <summary>
        /// Runs one inference and reports which model answered.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The oversize check happens before the backend is touched. A payload the
        /// Server has already said it will not take inline should be refused by the
        /// Server, not discovered by a remote endpoint - and the refusal names the
        /// transfer that will carry it, so a caller that reads the answer can act on
        /// it without a second round trip to work out what to do.
        /// </para>
        /// <para>
        /// When the primary fails and policy allows it, the fallback answers and
        /// <c>ModelUsed</c> names the fallback's model. That substitution is the
        /// entire reason the output exists: a caller that cannot see which model
        /// produced a result cannot tell a degraded answer from a good one, and a
        /// fallback that answers silently looks exactly like a healthy primary.
        /// </para>
        /// </remarks>
        private async ValueTask<InvokeMethodStateResult> InvokeAsync(
            NodeId objectId,
            ByteString payload,
            string payloadUri,
            string contentType,
            double timeout,
            CancellationToken ct)
        {
            DeploymentState? deployment = FindDeployment(objectId);
            if (deployment is null)
            {
                return new InvokeMethodStateResult
                {
                    ServiceResult = StatusCodes.BadNodeIdUnknown
                };
            }

            // Clause 8.4. Exactly one of Payload and PayloadUri says where the input
            // is; a call supplying both would not say which was read, and one
            // supplying neither carries no input at all.
            if (payload.IsNull == string.IsNullOrEmpty(payloadUri))
            {
                return new InvokeMethodStateResult
                {
                    ServiceResult = StatusCodes.BadInvalidArgument
                };
            }

            ReadOnlyMemory<byte> body = payload.Memory;

            if (body.Length > m_backendOptions.MaxInlinePayloadSize)
            {
                BeginTransferMethodStateResult transfer =
                    await BeginTransferAsync(
                        objectId,
                        contentType,
                        (ulong)body.Length,
                        ct).ConfigureAwait(false);

                if (!transfer.Accepted)
                {
                    // The refusal travels. Reporting Good with a null Transfer would
                    // tell the caller "too large for inline, use transfer null",
                    // which is indistinguishable from a real transfer it cannot
                    // find - and once MaxConcurrentTransfers is reached that is the
                    // ordinary outcome rather than an edge case.
                    return new InvokeMethodStateResult
                    {
                        ServiceResult = transfer.ServiceResult,
                        ResponsePayload = ByteString.Empty,
                        ResponseContentType = string.Empty,
                        ModelUsed = NodeId.Null,
                        Usage = new UsageDataType(),
                        SafetyAssessment = ArrayOf<SafetyAssessmentDataType>.Empty,
                        TransferRequired = true,
                        Transfer = NodeId.Null
                    };
                }

                return new InvokeMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    ResponsePayload = ByteString.Empty,
                    ResponseContentType = string.Empty,
                    ModelUsed = NodeId.Null,
                    Usage = new UsageDataType(),
                    FinishReason = FinishReasonEnum.Length,
                    SafetyAssessment = ArrayOf<SafetyAssessmentDataType>.Empty,
                    TransferRequired = true,
                    Transfer = transfer.Transfer
                };
            }

            InferenceOutcome outcome = await RunWithFallbackAsync(
                deployment,
                body,
                contentType,
                TimeoutOrDefault(timeout),
                ct).ConfigureAwait(false);

            if (!outcome.Result.Ok)
            {
                return new InvokeMethodStateResult
                {
                    ServiceResult = outcome.Result.RetryAfter > TimeSpan.Zero
                        ? StatusCodes.BadTooManyOperations
                        : StatusCodes.BadRequestNotAllowed,
                    RetryAfter = outcome.Result.RetryAfter.TotalMilliseconds,
                    ModelUsed = NodeId.Null,
                    Usage = new UsageDataType(),
                    SafetyAssessment = ArrayOf<SafetyAssessmentDataType>.Empty,
                    ResponsePayload = ByteString.Empty,
                    ResponseContentType = string.Empty,
                    Transfer = NodeId.Null
                };
            }

            return new InvokeMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                ResponsePayload = new ByteString(outcome.Result.Payload.ToArray()),
                ResponseContentType = outcome.Result.ContentType,
                ModelUsed = outcome.ModelUsed,
                Usage = ToUsage(outcome.Result),
                FinishReason = ToFinishReason(outcome.Result.Finish),
                SafetyAssessment = ArrayOf<SafetyAssessmentDataType>.Empty,
                RetryAfter = 0,
                TransferRequired = false,
                Transfer = NodeId.Null
            };
        }

        /// <summary>
        /// What a deployment can do, answered from the backend rather than from
        /// configuration.
        /// </summary>
        /// <remarks>
        /// Configuration says what an operator believes; the backend says what is
        /// actually there. Where they disagree the second one is the one a caller
        /// needs, so the probe is what decides <c>Reachable</c> here.
        /// </remarks>
        private async ValueTask<GetCapabilitiesMethodStateResult> GetCapabilitiesAsync(
            NodeId objectId,
            CancellationToken ct)
        {
            DeploymentState? deployment = FindDeployment(objectId);
            if (deployment is null)
            {
                return new GetCapabilitiesMethodStateResult
                {
                    ServiceResult = StatusCodes.BadNodeIdUnknown,
                    Capabilities = ArrayOf<CapabilityDataType>.Empty
                };
            }

            IInferenceBackend backend = BackendFor(deployment);
            BackendProbe probe = await backend.ProbeAsync(ct).ConfigureAwait(false);

            IReadOnlyList<BackendModel> models = probe.Reachable
                ? await backend.ListModelsAsync(null, 16, ct).ConfigureAwait(false)
                : Array.Empty<BackendModel>();

            var capabilities = new List<CapabilityDataType>
            {
                new() { Name = "reachable", Supported = probe.Reachable },
                new() { Name = "inline-payload", Supported = true },
                new() { Name = "chunked-transfer", Supported = true },
                new()
                {
                    Name = "async-inference",
                    Supported = true
                }
            };

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (BackendModel model in models)
            {
                foreach (string capability in model.Capabilities)
                {
                    if (seen.Add(capability))
                    {
                        capabilities.Add(new CapabilityDataType
                        {
                            Name = capability,
                            Supported = true
                        });
                    }
                }
            }

            UpdateReachability(deployment, probe.Reachable);

            return new GetCapabilitiesMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Capabilities = new ArrayOf<CapabilityDataType>(capabilities.ToArray())
            };
        }

        private static double TimeoutOrDefault(double timeoutMilliseconds)
        {
            return timeoutMilliseconds > 0 ? timeoutMilliseconds : 30000;
        }

        private static UsageDataType ToUsage(InferenceResult result)
        {
            return new UsageDataType
            {
                UnitKind = result.UsageUnit,
                InputUnits = result.InputUnits,
                OutputUnits = result.OutputUnits,
                TotalUnits = result.TotalUnits
            };
        }

        /// <summary>
        /// Maps the backend's finish reason onto the model's.
        /// </summary>
        /// <remarks>
        /// The two enumerations carry the same members, which is not a coincidence:
        /// the backend abstraction was written against the specification. Mapping
        /// explicitly rather than casting keeps it that way, because a cast would
        /// silently produce nonsense the day either side gained a member.
        /// </remarks>
        private static FinishReasonEnum ToFinishReason(InferenceFinish finish)
        {
            return finish switch
            {
                InferenceFinish.Length => FinishReasonEnum.Length,
                InferenceFinish.ToolCall => FinishReasonEnum.ToolCall,
                InferenceFinish.Filtered => FinishReasonEnum.Filtered,
                InferenceFinish.Cancelled => FinishReasonEnum.Cancelled,
                InferenceFinish.Error => FinishReasonEnum.Error,
                _ => FinishReasonEnum.Stop
            };
        }
    }
}
