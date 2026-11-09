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
using Opc.Ua.Client;

namespace Opc.Ua.Positioning.Client
{
    /// <summary>
    /// Shares Positioning protocol reads without taking ownership of the session.
    /// </summary>
    internal sealed partial class PositioningClientOperations
    {
        /// <summary>
        /// Creates legacy operations, or strict operations when options are supplied.
        /// </summary>
        public PositioningClientOperations(
            ISession session,
            ITelemetryContext telemetry,
            PositioningClientReadOptions? options = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            options?.Validate();
            m_options = options;
        }

        /// <summary>
        /// Gets the borrowed session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the caller's telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets whether strict validation and collection limits are enabled.
        /// </summary>
        public bool Strict => m_options is not null;

        /// <summary>
        /// Gets the frame-chain bound, preserving the historical legacy limit.
        /// </summary>
        public int MaximumFrameChainLength => m_options?.MaximumFrameChainLength ?? 1024;

        /// <summary>
        /// Collects one complete browse, with strict bounds when configured.
        /// </summary>
        public async ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            uint nodeClassMask,
            CancellationToken cancellationToken)
        {
            if (Strict)
            {
                return await BrowseBoundedAsync(
                    nodeId,
                    referenceTypeId,
                    nodeClassMask,
                    cancellationToken).ConfigureAwait(false);
            }

            (
                ArrayOf<ArrayOf<ReferenceDescription>> results,
                ArrayOf<ServiceResult> errors
            ) = await Session.ManagedBrowseAsync(
                requestHeader: null,
                view: null,
                nodesToBrowse: [nodeId],
                maxResultsToReturn: 0,
                browseDirection: BrowseDirection.Forward,
                referenceTypeId: referenceTypeId,
                includeSubtypes: true,
                nodeClassMask: nodeClassMask,
                ct: cancellationToken).ConfigureAwait(false);

            if (errors.Count > 0 && ServiceResult.IsBad(errors[0]))
            {
                throw new ServiceResultException(errors[0]);
            }

            return results.Count > 0
                ? results[0]
                : ArrayOf<ReferenceDescription>.Empty;
        }

        /// <summary>
        /// Resolves an optional child; only BadNoMatch denotes absence in strict mode.
        /// </summary>
        public async ValueTask<NodeId> ResolveChildAsync(
            NodeId parentId,
            NodeId referenceTypeId,
            QualifiedName browseName,
            CancellationToken cancellationToken)
        {
            ValidateNodeId(parentId);
            cancellationToken.ThrowIfCancellationRequested();
            var path = new BrowsePath
            {
                StartingNode = parentId,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = referenceTypeId,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = browseName
                        }
                    ]
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    requestHeader: null,
                    browsePaths: [path],
                    ct: cancellationToken).ConfigureAwait(false);

            if (Strict)
            {
                ValidateResponse(response.ResponseHeader, response.Results.Count, 1, response.DiagnosticInfos.Count);
                cancellationToken.ThrowIfCancellationRequested();
                BrowsePathResult result = response.Results[0] ??
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "A browse path result is missing.");
                if (result.StatusCode == StatusCodes.BadNoMatch && result.Targets.Count == 0)
                {
                    return NodeId.Null;
                }
                RequireGood(result.StatusCode);
                if (result.Targets.Count != 1 ||
                    result.Targets[0] is not { RemainingPathIndex: uint.MaxValue } target)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "A Positioning path must have one complete target.");
                }
                return LocalNodeId(target.TargetId);
            }

            if (response.Results.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode) ||
                response.Results[0].Targets.Count == 0)
            {
                return NodeId.Null;
            }

            return ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId,
                Session.NamespaceUris);
        }

        /// <summary>
        /// Resolves a mandatory child without replacing a service error with absence.
        /// </summary>
        public async ValueTask<NodeId> ResolveRequiredChildAsync(
            NodeId parentId,
            NodeId referenceTypeId,
            QualifiedName browseName,
            CancellationToken cancellationToken)
        {
            NodeId nodeId = await ResolveChildAsync(
                parentId,
                referenceTypeId,
                browseName,
                cancellationToken).ConfigureAwait(false);
            if (nodeId.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoMatch,
                    $"Node '{parentId}' does not expose '{browseName}'.");
            }
            return nodeId;
        }

        /// <summary>
        /// Reads a generated structure, validating quality before decoding in strict mode.
        /// </summary>
        /// <typeparam name="T">The expected generated structure type.</typeparam>
        public async ValueTask<T> ReadStructureAsync<T>(
            NodeId nodeId,
            CancellationToken cancellationToken)
            where T : class, IEncodeable
        {
            DataValue dataValue = await ReadValueAsync(
                nodeId,
                cancellationToken).ConfigureAwait(false);
            return DecodeStructure<T>(dataValue);
        }

        /// <summary>
        /// Bounds the raw array before allocating and decoding its typed representation.
        /// </summary>
        /// <typeparam name="T">The expected generated element type.</typeparam>
        public async ValueTask<ArrayOf<T>> ReadStructureArrayAsync<T>(
            NodeId nodeId,
            CancellationToken cancellationToken)
            where T : class, IEncodeable
        {
            DataValue dataValue = await ReadValueAsync(
                nodeId,
                cancellationToken).ConfigureAwait(false);
            if (Strict)
            {
                if (dataValue.WrappedValue.TypeInfo.ValueRank != ValueRanks.OneDimension ||
                    !dataValue.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> raw))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTypeMismatch,
                        "A one-dimensional structure array is required.");
                }
                if (raw.Count > m_options!.MaximumReadArrayItems)
                {
                    throw LimitExceeded("The Positioning structure array item limit was exceeded.");
                }
            }
            if (!dataValue.WrappedValue.TryGetValue(
                out ArrayOf<T> value,
                Session.MessageContext))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    $"Node '{nodeId}' does not contain an array of {typeof(T).Name} values.");
            }
            if (Strict)
            {
                for (int ii = 0; ii < value.Count; ii++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (value[ii] is null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTypeMismatch,
                            "A Positioning structure array contains a null element.");
                    }
                }
            }
            return value;
        }

        /// <summary>
        /// Reads a NodeId without erasing a metadata quality error.
        /// </summary>
        public async ValueTask<NodeId> ReadNodeIdAsync(
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            DataValue dataValue = await ReadValueAsync(
                nodeId,
                cancellationToken).ConfigureAwait(false);
            return DecodeNodeId(dataValue);
        }

        /// <summary>
        /// Reads an exact scalar UInt32 in strict mode, never reinterpreting a StatusCode.
        /// </summary>
        public async ValueTask<uint> ReadUInt32Async(
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            DataValue dataValue = await ReadValueAsync(
                nodeId,
                cancellationToken).ConfigureAwait(false);
            if ((Strict &&
                (dataValue.WrappedValue.TypeInfo.BuiltInType != BuiltInType.UInt32 ||
                    !dataValue.WrappedValue.TypeInfo.IsScalar)) ||
                !dataValue.WrappedValue.TryGetValue(out uint value))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    $"Node '{nodeId}' does not contain a UInt32 value.");
            }
            return value;
        }

        /// <summary>
        /// Converts a discovered entry without accepting remote or unmapped targets in strict mode.
        /// </summary>
        public PositioningObjectEntry CreateEntry(
            ReferenceDescription reference)
        {
            NodeId nodeId = LocalNodeId(reference.NodeId);
            NodeId typeDefinitionId = LocalNodeId(reference.TypeDefinition);
            return new PositioningObjectEntry(
                nodeId,
                reference.BrowseName,
                reference.DisplayName,
                typeDefinitionId);
        }

        /// <summary>
        /// Gets an existing model namespace without changing the table in strict mode.
        /// </summary>
        public ushort GetNamespaceIndex(string namespaceUri)
        {
            if (!Strict)
            {
                return Session.NamespaceUris.GetIndexOrAppend(namespaceUri);
            }
            int index = Session.NamespaceUris.GetIndex(namespaceUri);
            if (index < 0 || index > ushort.MaxValue)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown,
                    $"The server does not expose namespace '{namespaceUri}'.");
            }
            return (ushort)index;
        }

        /// <summary>
        /// Reads a value using the original overload, or an exact validated raw response.
        /// </summary>
        public async ValueTask<DataValue> ReadValueAsync(NodeId nodeId, CancellationToken cancellationToken)
        {
            if (!Strict)
            {
                return await Session.ReadValueAsync(nodeId, cancellationToken).ConfigureAwait(false);
            }
            ArrayOf<DataValue> values = await ReadValuesAsync([nodeId], cancellationToken).ConfigureAwait(false);
            return values[0];
        }

        /// <summary>
        /// Reads a fixed-width batch with Good quality and both timestamps.
        /// A single request reduces skew, but does not imply server-side snapshot isolation.
        /// </summary>
        public async ValueTask<ArrayOf<DataValue>> ReadValuesAsync(
            ArrayOf<NodeId> nodeIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                ValidateNodeId(nodeIds[ii]);
            }
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                nodeIds.ConvertAll(nodeId => new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }),
                cancellationToken).ConfigureAwait(false);
            ValidateResponse(
                response.ResponseHeader,
                response.Results.Count,
                nodeIds.Count,
                response.DiagnosticInfos.Count);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (DataValue value in response.Results)
            {
                ValidateValue(value);
            }
            return response.Results;
        }

        /// <summary>
        /// Decodes a structure only after validating its quality when strict reads are enabled.
        /// </summary>
        /// <typeparam name="T">The expected generated structure type.</typeparam>
        public T DecodeStructure<T>(in DataValue dataValue)
            where T : class, IEncodeable
        {
            ValidateValue(dataValue);
            if (!dataValue.WrappedValue.TryGetValue<T>(out T? value, Session.MessageContext) ||
                (Strict && value is null))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    $"A {typeof(T).Name} value is required.");
            }
            return value!;
        }

        /// <summary>
        /// Decodes a NodeId, allowing the null NodeId to denote the RSL world frame.
        /// </summary>
        public NodeId DecodeNodeId(in DataValue dataValue)
        {
            ValidateValue(dataValue);
            if (!dataValue.WrappedValue.TryGetValue(out NodeId value))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "A NodeId value is required.");
            }
            if (!value.IsNull)
            {
                ValidateNodeId(value);
            }
            return value;
        }

        /// <summary>
        /// Rejects bad and uncertain values instead of manufacturing a Good result.
        /// </summary>
        public void ValidateValue(in DataValue value)
        {
            if (Strict)
            {
                RequireGood(value.StatusCode);
            }
        }

        private readonly PositioningClientReadOptions? m_options;
    }
}
