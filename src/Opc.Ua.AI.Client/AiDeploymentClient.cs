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

namespace Opc.Ua.AI.Client
{
    public sealed class AIDeploymentClient
    {
        public AIDeploymentClient(AIClient client, NodeId deploymentNodeId)
            : this(client?.Operations ?? throw new ArgumentNullException(nameof(client)), deploymentNodeId)
        {
        }

        internal AIDeploymentClient(AIClientOperations operations, NodeId deploymentNodeId)
        {
            m_operations = operations ?? throw new ArgumentNullException(nameof(operations));
            if (deploymentNodeId.IsNull)
            {
                throw new ArgumentException("Deployment NodeId must not be null.", nameof(deploymentNodeId));
            }
            DeploymentNodeId = deploymentNodeId;
            m_proxy = new DeploymentTypeClient(
                m_operations.Session, deploymentNodeId, m_operations.Telemetry);
        }

        public NodeId DeploymentNodeId { get; }

        public async ValueTask<AIDeploymentSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.DeploymentId,
                BrowseNames.InferenceLocation,
                BrowseNames.State,
                BrowseNames.DataJurisdiction,
                BrowseNames.EgressPermitted,
                BrowseNames.MaxInlinePayloadSize,
                BrowseNames.EndpointUri
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                DeploymentNodeId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(nodes, cancellationToken)
                .ConfigureAwait(false);
            int cursor = 0;
            NodeId model = await m_operations.FollowReferenceAsync(
                DeploymentNodeId, ReferenceTypes.UsesModel, cancellationToken).ConfigureAwait(false);
            NodeId fallback = await m_operations.FollowReferenceAsync(
                DeploymentNodeId, ReferenceTypes.FallsBackTo, cancellationToken).ConfigureAwait(false);
            return new AIDeploymentSnapshot
            {
                NodeId = DeploymentNodeId,
                DeploymentId = ReadString(nodes, values, ref cursor, 0),
                InferenceLocation = ReadEnum<InferenceLocationEnum>(nodes, values, ref cursor, 1),
                State = ReadEnum<DeploymentStateEnum>(nodes, values, ref cursor, 2),
                DataJurisdiction = ReadString(nodes, values, ref cursor, 3),
                EgressPermitted = ReadBoolean(nodes, values, ref cursor, 4),
                MaxInlinePayloadSize = ReadUInt64(nodes, values, ref cursor, 5),
                EndpointUri = ReadString(nodes, values, ref cursor, 6),
                ModelId = model,
                FallbackDeploymentId = fallback
            };
        }

        public ValueTask<ArrayOf<CapabilityDataType>> GetCapabilitiesAsync(
            CancellationToken cancellationToken = default)
        {
            return GetCapabilitiesCoreAsync(cancellationToken);
        }

        public async ValueTask<AIInvokeResult> InvokeAsync(
            ByteString payload,
            string contentType,
            ArrayOf<global::Opc.Ua.KeyValuePair> parameters,
            double timeout,
            string payloadUri = "",
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await InvokeProxyAsync(
                    payload, contentType, parameters, timeout, payloadUri, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadMethodInvalid)
            {
                ArrayOf<Variant> outputs = await m_operations.CallAsync(
                    DeploymentNodeId,
                    BrowseNames.Invoke,
                    [
                        Variant.From(payload),
                        Variant.From(payloadUri ?? string.Empty),
                        Variant.From(contentType ?? string.Empty),
                        Variant.FromStructure(parameters),
                        Variant.From(timeout)
                    ],
                    cancellationToken).ConfigureAwait(false);
                return CreateInvokeResult(outputs);
            }
        }

        public ValueTask<NodeId> InvokeAsyncAsync(
            ByteString payload,
            string contentType,
            ArrayOf<global::Opc.Ua.KeyValuePair> parameters,
            string payloadUri = "",
            CancellationToken cancellationToken = default)
        {
            return InvokeAsyncCoreAsync(payload, contentType, parameters, payloadUri, cancellationToken);
        }

        public async ValueTask<AIBeginTransferResult> BeginTransferAsync(
            string contentType,
            ulong requestSize,
            CancellationToken cancellationToken = default)
        {
            try
            {
                (NodeId transfer, bool accepted) = await m_proxy.BeginTransferAsync(
                    contentType ?? string.Empty, requestSize, cancellationToken).ConfigureAwait(false);
                return new AIBeginTransferResult
                {
                    TransferId = transfer,
                    Accepted = accepted
                };
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadMethodInvalid)
            {
                ArrayOf<Variant> outputs = await m_operations.CallAsync(
                    DeploymentNodeId,
                    BrowseNames.BeginTransfer,
                    [Variant.From(contentType ?? string.Empty), Variant.From(requestSize)],
                    cancellationToken).ConfigureAwait(false);
                return new AIBeginTransferResult
                {
                    TransferId = TryGetNodeId(outputs, 0, out NodeId transfer) ? transfer : NodeId.Null,
                    Accepted = TryGetBoolean(outputs, 1, out bool accepted) && accepted
                };
            }
        }

        public async ValueTask<AIModelClient?> OpenModelAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId model = await m_operations.FollowReferenceAsync(
                DeploymentNodeId, ReferenceTypes.UsesModel, cancellationToken).ConfigureAwait(false);
            return model.IsNull ? null : new AIModelClient(m_operations, model);
        }

        public async ValueTask<AIDeploymentClient?> OpenFallbackAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId fallback = await m_operations.FollowReferenceAsync(
                DeploymentNodeId, ReferenceTypes.FallsBackTo, cancellationToken).ConfigureAwait(false);
            return fallback.IsNull ? null : new AIDeploymentClient(m_operations, fallback);
        }

        internal static void ThrowIfBad(ServiceResult serviceResult)
        {
            if (serviceResult is not null && ServiceResult.IsBad(serviceResult))
            {
                throw new ServiceResultException(serviceResult);
            }
        }

        private async ValueTask<ArrayOf<CapabilityDataType>> GetCapabilitiesCoreAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                return await m_proxy.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadMethodInvalid)
            {
                ArrayOf<Variant> outputs = await m_operations.CallAsync(
                    DeploymentNodeId,
                    BrowseNames.GetCapabilities,
                    ArrayOf<Variant>.Empty,
                    cancellationToken).ConfigureAwait(false);
                if (outputs.Count > 0 &&
                    outputs[0].TryGetValue(
                        out ArrayOf<CapabilityDataType> capabilities,
                        m_operations.Session.MessageContext))
                {
                    return capabilities;
                }
                return ArrayOf<CapabilityDataType>.Empty;
            }
        }

        private async ValueTask<AIInvokeResult> InvokeProxyAsync(
            ByteString payload,
            string contentType,
            ArrayOf<global::Opc.Ua.KeyValuePair> parameters,
            double timeout,
            string payloadUri,
            CancellationToken cancellationToken)
        {
            (
                ByteString responsePayload,
                string responseContentType,
                NodeId modelUsed,
                UsageDataType usage,
                FinishReasonEnum finishReason,
                ArrayOf<SafetyAssessmentDataType> safetyAssessment,
                double retryAfter,
                bool transferRequired,
                NodeId transfer) = await m_proxy.InvokeAsync(
                payload,
                payloadUri ?? string.Empty,
                contentType ?? string.Empty,
                parameters,
                timeout,
                cancellationToken).ConfigureAwait(false);
            return new AIInvokeResult
            {
                ResponsePayload = responsePayload,
                ResponseContentType = responseContentType,
                ModelUsed = modelUsed,
                Usage = usage,
                FinishReason = finishReason,
                SafetyAssessment = safetyAssessment,
                RetryAfter = retryAfter,
                TransferRequired = transferRequired,
                TransferId = transfer
            };
        }

        private async ValueTask<NodeId> InvokeAsyncCoreAsync(
            ByteString payload,
            string contentType,
            ArrayOf<global::Opc.Ua.KeyValuePair> parameters,
            string payloadUri,
            CancellationToken cancellationToken)
        {
            try
            {
                return await m_proxy.InvokeAsyncAsync(
                    payload,
                    payloadUri ?? string.Empty,
                    contentType ?? string.Empty,
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadMethodInvalid)
            {
                ArrayOf<Variant> outputs = await m_operations.CallAsync(
                    DeploymentNodeId,
                    BrowseNames.InvokeAsync,
                    [
                        Variant.From(payload),
                        Variant.From(payloadUri ?? string.Empty),
                        Variant.From(contentType ?? string.Empty),
                        Variant.FromStructure(parameters)
                    ],
                    cancellationToken).ConfigureAwait(false);
                return TryGetNodeId(outputs, 0, out NodeId job) ? job : NodeId.Null;
            }
        }

        private AIInvokeResult CreateInvokeResult(ArrayOf<Variant> outputs)
        {
            return new AIInvokeResult
            {
                ResponsePayload = TryGetByteString(outputs, 0, out ByteString responsePayload)
                    ? responsePayload
                    : ByteString.Empty,
                ResponseContentType = TryGetString(outputs, 1, out string? responseContentType)
                    ? responseContentType
                    : null,
                ModelUsed = TryGetNodeId(outputs, 2, out NodeId modelUsed) ? modelUsed : NodeId.Null,
                Usage = TryGetStructure(outputs, 3, out UsageDataType usage) ? usage : null,
                FinishReason = TryGetEnum(outputs, 4, out FinishReasonEnum finishReason)
                    ? finishReason
                    : default,
                SafetyAssessment = TryGetStructureArray(outputs, 5, out ArrayOf<SafetyAssessmentDataType> safety)
                    ? safety
                    : ArrayOf<SafetyAssessmentDataType>.Empty,
                RetryAfter = TryGetDouble(outputs, 6, out double retryAfter) ? retryAfter : 0,
                TransferRequired = TryGetBoolean(outputs, 7, out bool transferRequired) && transferRequired,
                TransferId = TryGetNodeId(outputs, 8, out NodeId transfer) ? transfer : NodeId.Null
            };
        }

        private bool TryGetStructure<T>(ArrayOf<Variant> outputs, int index, out T value)
            where T : class, IEncodeable
        {
#pragma warning disable CS8600 // TryGetValue uses [MaybeNullWhen(false)] on encodeable overloads.
            if (index < outputs.Count &&
                outputs[index].TryGetValue(out T result, m_operations.Session.MessageContext))
#pragma warning restore CS8600
            {
                value = result;
                return true;
            }
            value = null!;
            return false;
        }

        private bool TryGetStructureArray<T>(ArrayOf<Variant> outputs, int index, out ArrayOf<T> value)
            where T : class, IEncodeable
        {
            if (index < outputs.Count &&
                outputs[index].TryGetValue(out ArrayOf<T> result, m_operations.Session.MessageContext))
            {
                value = result;
                return true;
            }
            value = ArrayOf<T>.Empty;
            return false;
        }

        private static bool TryGetString(ArrayOf<Variant> outputs, int index, out string? value)
        {
            if (index < outputs.Count && outputs[index].TryGetValue(out string? result))
            {
                value = result;
                return true;
            }
            value = null;
            return false;
        }

        private static bool TryGetByteString(ArrayOf<Variant> outputs, int index, out ByteString value)
        {
            if (index < outputs.Count && outputs[index].TryGetValue(out ByteString result))
            {
                value = result;
                return true;
            }
            value = ByteString.Empty;
            return false;
        }

        private static bool TryGetNodeId(ArrayOf<Variant> outputs, int index, out NodeId value)
        {
            if (index < outputs.Count && outputs[index].TryGetValue(out NodeId result))
            {
                value = result;
                return true;
            }
            value = NodeId.Null;
            return false;
        }

        private static bool TryGetBoolean(ArrayOf<Variant> outputs, int index, out bool value)
        {
            if (index < outputs.Count && outputs[index].TryGetValue(out bool result))
            {
                value = result;
                return true;
            }
            value = false;
            return false;
        }

        private static bool TryGetDouble(ArrayOf<Variant> outputs, int index, out double value)
        {
            if (index < outputs.Count && outputs[index].TryGetValue(out double result))
            {
                value = result;
                return true;
            }
            value = 0;
            return false;
        }

        private static bool TryGetEnum<TEnum>(ArrayOf<Variant> outputs, int index, out TEnum value)
            where TEnum : struct, Enum
        {
            if (index < outputs.Count && outputs[index].TryGetValue(out int intValue))
            {
                value = (TEnum)Enum.ToObject(typeof(TEnum), intValue);
                return true;
            }
            if (index < outputs.Count && outputs[index].TryGetValue(out uint uintValue))
            {
                value = (TEnum)Enum.ToObject(typeof(TEnum), uintValue);
                return true;
            }
            value = default;
            return false;
        }

        private static string? ReadString(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? null : AIClientOperations.ReadString(values[cursor++]);
        }

        private static bool ReadBoolean(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return !nodes[index].IsNull && AIClientOperations.ReadBoolean(values[cursor++]);
        }

        private static ulong ReadUInt64(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? 0 : AIClientOperations.ReadUInt64(values[cursor++]);
        }

        private static TEnum ReadEnum<TEnum>(
            ArrayOf<NodeId> nodes,
            ArrayOf<DataValue> values,
            ref int cursor,
            int index)
            where TEnum : struct, Enum
        {
            if (nodes[index].IsNull)
            {
                return default;
            }
            return AIClientOperations.TryReadEnum(values[cursor++], out TEnum result)
                ? result
                : default;
        }

        private readonly AIClientOperations m_operations;
        private readonly DeploymentTypeClient m_proxy;
    }
}
