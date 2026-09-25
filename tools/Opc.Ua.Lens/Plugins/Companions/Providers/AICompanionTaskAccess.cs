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
using Opc.Ua;
using Opc.Ua.AI.Client;
using Opc.Ua.Client.StateMachines;
using Ai = Opc.Ua.AI;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Strict, bounded evidence for AI tasks. The catalogue client's optional-property
    /// defaults must not turn unreadable egress, lifecycle or permission values into authorization.
    /// All mutations remain on the existing generated or typed AI clients.
    /// </summary>
    internal static class AICompanionTaskAccess
    {
        public static CancellationTokenSource Begin(CompanionContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            RequireConnected(context, cancellationToken);
            return CellCompanionSupport.BeginOperation(context, Ai.Namespaces.AI, cancellationToken);
        }

        public static void RequireConnected(CompanionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.Session.Connected || context.Session.SessionId.IsNull)
            {
                throw new InvalidOperationException("The AI session is disconnected.");
            }
        }

        public static void RequireSecureChannel(CompanionContext context)
        {
            EndpointDescription endpoint = context.Session.Endpoint;
            if (endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                string.IsNullOrEmpty(endpoint.SecurityPolicyUri) ||
                endpoint.SecurityPolicyUri == SecurityPolicies.None)
            {
                throw new UnauthorizedAccessException("AI deployment tasks require a signed and encrypted session.");
            }
        }

        public static ValueTask RequireTargetAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            RequireConnected(context, cancellationToken);
            return IndustrialCompanionAccess.RequireTargetAsync(context, target, "ai", s_types, cancellationToken);
        }

        public static ValueTask RequireInstanceAsync(
            CompanionContext context, NodeId nodeId, string kind, CancellationToken cancellationToken)
        {
            if (nodeId.IsNull || nodeId.NamespaceIndex >= context.Session.NamespaceUris.Count)
            {
                throw new ArgumentException("Select an existing AI instance on this session.");
            }
            return RequireTargetAsync(
                context, new CompanionTarget("ai", nodeId, kind, kind), cancellationToken);
        }

        public static ValueTask<bool> CanExecuteAsync(
            CompanionContext context, NodeId parent, string methodName, CancellationToken cancellationToken)
        {
            return CanExecuteAsync(context, parent, methodName, Ai.Namespaces.AI, cancellationToken);
        }

        public static async ValueTask<bool> CanExecuteAsync(
            CompanionContext context, NodeId parent, string methodName, string namespaceUri,
            CancellationToken cancellationToken)
        {
            NodeId method = await IndustrialCompanionAccess.ResolveChildAsync(
                context, parent, namespaceUri, methodName, true, cancellationToken)
                .ConfigureAwait(false);
            if (method.IsNull)
            {
                return false;
            }
            ArrayOf<DataValue> values = await ReadAsync(context,
                [
                    new ReadValueId { NodeId = method, AttributeId = Attributes.Executable },
                    new ReadValueId { NodeId = method, AttributeId = Attributes.UserExecutable }
                ],
                cancellationToken).ConfigureAwait(false);
            bool executable = Boolean(values[0].WrappedValue);
            bool allowed = Boolean(values[1].WrappedValue);
            return executable && allowed;
        }

        public static ValueTask RequireExecutableAsync(
            CompanionContext context, NodeId parent, string methodName, CancellationToken cancellationToken)
        {
            return RequireExecutableAsync(context, parent, methodName, Ai.Namespaces.AI, cancellationToken);
        }

        public static async ValueTask RequireExecutableAsync(
            CompanionContext context, NodeId parent, string methodName, string namespaceUri,
            CancellationToken cancellationToken)
        {
            if (!await CanExecuteAsync(context, parent, methodName, namespaceUri, cancellationToken)
                .ConfigureAwait(false))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotExecutable, "The AI method is absent or not executable by this user.");
            }
        }

        public static async ValueTask<AIDeploymentSnapshot> ReadDeploymentAsync(
            CompanionContext context, NodeId deploymentId, CancellationToken cancellationToken)
        {
            ArrayOf<CompanionValue> values = await PropertiesAsync(context, deploymentId,
                [
                    Ai.BrowseNames.DeploymentId, Ai.BrowseNames.InferenceLocation, Ai.BrowseNames.State,
                    Ai.BrowseNames.DataJurisdiction, Ai.BrowseNames.EgressPermitted,
                    Ai.BrowseNames.MaxInlinePayloadSize, Ai.BrowseNames.EndpointUri
                ],
                cancellationToken).ConfigureAwait(false);
            int location = Integer(values[1].Value);
            int state = Integer(values[2].Value);
            if (location is < 0 or > 3 || state is < 0 or > 4)
            {
                throw InvalidData("The deployment contains an unknown state or inference location.");
            }
            NodeId model = await ReferenceAsync(
                context, deploymentId, Ai.ReferenceTypeIds.UsesModel, cancellationToken).ConfigureAwait(false);
            NodeId fallback = await ReferenceAsync(
                context, deploymentId, Ai.ReferenceTypeIds.FallsBackTo, cancellationToken).ConfigureAwait(false);
            return new AIDeploymentSnapshot
            {
                NodeId = deploymentId,
                DeploymentId = String(values[0].Value),
                InferenceLocation = (Ai.InferenceLocationEnum)location,
                State = (Ai.DeploymentStateEnum)state,
                DataJurisdiction = String(values[3].Value, required: false),
                EgressPermitted = Boolean(values[4].Value),
                MaxInlinePayloadSize = values[5].Value.TypeInfo.BuiltInType == BuiltInType.UInt32 &&
                    values[5].Value.TryGetValue(out uint inlineLimit) ? inlineLimit :
                        throw InvalidData("The AI deployment inline limit must be a UInt32."),
                EndpointUri = IsAbsent(values[6].Value) ? null : String(values[6].Value, required: false),
                ModelId = model,
                FallbackDeploymentId = fallback
            };
        }

        public static async ValueTask<FiniteStateSnapshot> ReadJobStateAsync(
            CompanionContext context, NodeId jobId, CancellationToken cancellationToken)
        {
            var client = new ProgramStateMachineTypeClient(context.Session, jobId, context.Telemetry);
            FiniteStateSnapshot state = await client.GetCurrentFiniteStateAsync(cancellationToken)
                .ConfigureAwait(false);
            RequireGood(state.Status);
            if (state.StateMachineId != jobId || state.CurrentState.IsNull || state.CurrentStateId.IsNull)
            {
                throw InvalidData("The job does not expose a usable program state.");
            }

            // The finite-state helper preserves CurrentState quality, but not the Id's quality.
            NodeId current = await IndustrialCompanionAccess.ResolveChildAsync(
                context, jobId, Namespaces.OpcUa, BrowseNames.CurrentState, false, cancellationToken)
                .ConfigureAwait(false);
            NodeId id = await IndustrialCompanionAccess.ResolveChildAsync(
                context, current, Namespaces.OpcUa, BrowseNames.Id, false, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadAsync(context,
                [
                    new ReadValueId { NodeId = current, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = id, AttributeId = Attributes.Value }
                ],
                cancellationToken).ConfigureAwait(false);
            if (!values[0].WrappedValue.TryGetValue(out LocalizedText name) ||
                name.IsNull ||
                Node(values[1].WrappedValue) != state.CurrentStateId ||
                name != state.CurrentState)
            {
                throw new InvalidOperationException("The job state changed during observation. Observe it again.");
            }
            return state;
        }

        public static async ValueTask<AITaskLearningSnapshot> ReadLearningAsync(
            CompanionContext context, NodeId jobId, CancellationToken cancellationToken)
        {
            ArrayOf<CompanionValue> values = await PropertiesAsync(context, jobId,
                [
                    Ai.BrowseNames.JobId, Ai.BrowseNames.State, Ai.BrowseNames.BaseModel, Ai.BrowseNames.Dataset,
                    Ai.BrowseNames.CandidateModel, Ai.BrowseNames.TargetDeployment
                ],
                cancellationToken).ConfigureAwait(false);
            int phase = Integer(values[1].Value);
            if (phase is < 0 or > 7)
            {
                throw InvalidData("The learning job contains an unknown phase.");
            }
            return new AITaskLearningSnapshot(
                new AILearningJobSnapshot
                {
                    NodeId = jobId,
                    JobId = String(values[0].Value),
                    State = (Ai.LearningJobStateEnum)phase,
                    CandidateModelId = OptionalNode(values[4].Value),
                    TargetDeploymentId = OptionalNode(values[5].Value)
                },
                OptionalNode(values[2].Value),
                OptionalNode(values[3].Value));
        }

        public static async ValueTask<AIInferenceJobSnapshot> ReadInferenceResultAsync(
            CompanionContext context, NodeId jobId, CancellationToken cancellationToken)
        {
            ArrayOf<CompanionValue> values = await PropertiesAsync(context, jobId,
                [
                    Ai.BrowseNames.JobId, Ai.BrowseNames.Deployment, Ai.BrowseNames.ResponsePayload,
                    Ai.BrowseNames.ResponseContentType, Ai.BrowseNames.ModelUsed, Ai.BrowseNames.FinishReason
                ],
                cancellationToken).ConfigureAwait(false);
            if (!values[2].Value.TryGetValue(out ByteString payload))
            {
                throw InvalidData("The terminal inference response is missing or has the wrong type.");
            }
            int finishReason = Integer(values[5].Value);
            if (finishReason is < 0 or > 5)
            {
                throw InvalidData("The job result has an unknown finish reason.");
            }
            return new AIInferenceJobSnapshot
            {
                NodeId = jobId,
                JobId = String(values[0].Value),
                DeploymentId = Node(values[1].Value),
                ResponsePayload = payload,
                ResponseContentType = String(values[3].Value, required: false),
                ModelUsed = OptionalNode(values[4].Value),
                FinishReason = (Ai.FinishReasonEnum)finishReason
            };
        }

        public static async ValueTask<AITransferSnapshot> ReadTransferAsync(
            CompanionContext context, NodeId transferId, TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            ArrayOf<CompanionValue> values = await PropertiesAsync(context, transferId,
                [
                    Ai.BrowseNames.TransferId, Ai.BrowseNames.State, Ai.BrowseNames.ModelUsed,
                    Ai.BrowseNames.ResponseContentType, Ai.BrowseNames.ExpiresAt
                ],
                cancellationToken).ConfigureAwait(false);
            int state = Integer(values[1].Value);
            if (state is < 0 or > 5)
            {
                throw InvalidData("The transfer contains an unknown state.");
            }
            if (!IsAbsent(values[4].Value))
            {
                if (!values[4].Value.TryGetValue(out DateTimeUtc expires))
                {
                    throw InvalidData("The transfer expiry is not a timestamp.");
                }
                if ((DateTime)expires <= timeProvider.GetUtcNow().UtcDateTime)
                {
                    throw new InvalidOperationException("The transfer has expired; no file was opened.");
                }
            }
            return new AITransferSnapshot
            {
                NodeId = transferId,
                TransferId = String(values[0].Value),
                State = (Ai.TransferStateEnum)state,
                ModelUsed = OptionalNode(values[2].Value),
                ResponseContentType = IsAbsent(values[3].Value)
                    ? null : String(values[3].Value, required: false)
            };
        }

        public static ValueTask<ArrayOf<CompanionValue>> PropertiesAsync(
            CompanionContext context, NodeId parent, ArrayOf<string> names, CancellationToken cancellationToken)
        {
            return PropertiesAsync(context, parent, names, Ai.Namespaces.AI, cancellationToken);
        }

        public static async ValueTask<ArrayOf<CompanionValue>> PropertiesAsync(
            CompanionContext context, NodeId parent, ArrayOf<string> names, string namespaceUri,
            CancellationToken cancellationToken)
        {
            RequireConnected(context, cancellationToken);
            IndustrialCompanionAccess.CheckFields(context, names.Count);
            var reads = new List<ReadValueId>();
            var positions = new List<int>();
            var fields = new CompanionValue[names.Count];
            for (int index = 0; index < names.Count; index++)
            {
                NodeId node = await IndustrialCompanionAccess.ResolveChildAsync(
                    context, parent, namespaceUri, names[index], true, cancellationToken)
                    .ConfigureAwait(false);
                if (node.IsNull)
                {
                    fields[index] = new CompanionValue(names[index], Variant.From(StatusCodes.BadNotFound));
                    continue;
                }
                reads.Add(new ReadValueId { NodeId = node, AttributeId = Attributes.Value });
                positions.Add(index);
            }
            if (reads.Count > 0)
            {
                ArrayOf<DataValue> values = await ReadAsync(context, [.. reads], cancellationToken)
                    .ConfigureAwait(false);
                for (int index = 0; index < values.Count; index++)
                {
                    int position = positions[index];
                    fields[position] = new CompanionValue(names[position], values[index].WrappedValue);
                }
            }
            return fields;
        }

        public static async ValueTask<ArrayOf<DataValue>> ReadAsync(
            CompanionContext context, ArrayOf<ReadValueId> reads, CancellationToken cancellationToken)
        {
            RequireConnected(context, cancellationToken);
            ReadResponse response = await context.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, reads, cancellationToken).ConfigureAwait(false);
            if (response.ResponseHeader is null || response.Results.Count != reads.Count)
            {
                throw InvalidData("The AI read response is incomplete.");
            }
            RequireGood(response.ResponseHeader.ServiceResult);
            foreach (DataValue value in response.Results)
            {
                RequireGood(value.StatusCode);
            }
            return response.Results;
        }

        public static string String(Variant value, bool required = true)
        {
            if (!value.TryGetValue(out string? result) ||
                result is null ||
                result.Length > 2048 ||
                (required && string.IsNullOrWhiteSpace(result)))
            {
                throw InvalidData("An AI string is missing, oversized or has the wrong type.");
            }
            return result;
        }

        public static bool Boolean(Variant value)
        {
            return value.TryGetValue(out bool result)
                ? result : throw InvalidData("An AI Boolean is missing or has the wrong type.");
        }

        public static int Integer(Variant value)
        {
            return value.TryGetValue(out int result)
                ? result : throw InvalidData("An AI enumeration is missing or has the wrong type.");
        }

        public static ulong Unsigned(Variant value)
        {
            return value.TryGetValue(out ulong result)
                ? result : throw InvalidData("An AI UInt64 is missing or has the wrong type.");
        }

        public static NodeId Node(Variant value)
        {
            return value.TryGetValue(out NodeId result) && !result.IsNull
                ? result : throw InvalidData("An AI NodeId is missing or has the wrong type.");
        }

        public static NodeId OptionalNode(Variant value)
        {
            if (IsAbsent(value))
            {
                return NodeId.Null;
            }
            return value.TryGetValue(out NodeId result)
                ? result : throw InvalidData("An AI NodeId has the wrong type.");
        }

        public static bool IsAbsent(Variant value)
        {
            return value.IsNull ||
                (value.TryGetValue(out StatusCode status) && status == StatusCodes.BadNotFound);
        }

        public static bool IsTerminal(NodeId stateId)
        {
            return stateId == ObjectIds.ProgramStateMachineType_Halted;
        }

        public static void RequireGood(StatusCode status)
        {
            if (!StatusCode.IsGood(status))
            {
                throw new ServiceResultException(status);
            }
        }

        public static ServiceResultException InvalidData(string message)
        {
            return new ServiceResultException(StatusCodes.BadDecodingError, message);
        }

        private static async ValueTask<NodeId> ReferenceAsync(
            CompanionContext context, NodeId parent, ExpandedNodeId referenceType, CancellationToken cancellationToken)
        {
            var budget = new IndustrialBrowseBudget(context);
            NodeId result = NodeId.Null;
            var type = ExpandedNodeId.ToNodeId(referenceType, context.Session.NamespaceUris);
            if (type.IsNull)
            {
                throw InvalidData("The AI reference namespace is unavailable.");
            }
            await foreach (ReferenceDescription reference in IndustrialCompanionAccess.BrowseAsync(
                context, parent, BrowseDirection.Forward, type, NodeClass.Object, budget, cancellationToken)
                .ConfigureAwait(false))
            {
                NodeId child = IndustrialCompanionAccess.LocalId(context, reference.NodeId);
                if (!result.IsNull || child.IsNull)
                {
                    throw InvalidData("The deployment has an ambiguous or remote model reference.");
                }
                result = child;
            }
            return result;
        }

        private static readonly ArrayOf<IndustrialCompanionType> s_types =
        [
            new(Ai.ObjectTypeIds.ModelType, "Model"),
            new(Ai.ObjectTypeIds.DeploymentType, "Deployment"),
            new(Ai.ObjectTypeIds.DatasetType, "Dataset"),
            new(Ai.ObjectTypeIds.InferenceJobType, "Inference job"),
            new(Ai.ObjectTypeIds.LearningJobType, "Learning job"),
            new(Ai.ObjectTypeIds.InferenceTransferType, "Transfer"),
            new(Ai.ObjectTypeIds.EvaluationRunType, "Evaluation")
        ];
    }

    internal sealed record AITaskLearningSnapshot(AILearningJobSnapshot Job, NodeId BaseModelId, NodeId DatasetId);
}
