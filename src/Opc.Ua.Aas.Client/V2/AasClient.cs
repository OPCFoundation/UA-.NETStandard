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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Aas.V2;
using Opc.Ua.Client;

namespace Opc.Ua.Aas.Client.V2
{
    /// <summary>
    /// High-level client for the OPC UA AAS V2 metamodel defined by OPC 30270.
    /// </summary>
    /// <remarks>
    /// OPC 30270 leaves instance NodeIds server-specific. This client deliberately uses the shared deterministic AAS
    /// identifier and <c>idShortPath</c> encoding that the V2 materializer emits, so V2 and V3 use one addressing scheme.
    /// </remarks>
    public sealed class AasClient
    {
        /// <summary>
        /// Default per-call chunk size for FileType reads.
        /// </summary>
        public const int DefaultFileChunkSize = 4096;

        /// <summary>
        /// Creates a metamodel client over an open OPC UA session.
        /// </summary>
        /// <param name="session">The OPC UA session.</param>
        /// <param name="instanceNamespaceIndex">The namespace index that contains materialized AAS V2 instance nodes.</param>
        /// <param name="telemetry">Telemetry context used by generated proxies.</param>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> or <paramref name="telemetry"/> is <c>null</c>.</exception>
        public AasClient(ISession session, ushort instanceNamespaceIndex, ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            Session = session;
            InstanceNamespaceIndex = instanceNamespaceIndex;
            Telemetry = telemetry;
        }

        /// <summary>
        /// The OPC UA session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Namespace index that contains materialized AAS V2 instance nodes.
        /// </summary>
        public ushort InstanceNamespaceIndex { get; }

        /// <summary>
        /// Telemetry context used by generated proxies.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Computes the NodeId of an AAS V2 shell from its identifier and opens a generated proxy.
        /// </summary>
        public AASAssetAdministrationShellTypeClient OpenShell(string id)
        {
            return new AASAssetAdministrationShellTypeClient(Session, CreateShellNodeId(id), Telemetry);
        }

        /// <summary>
        /// Computes the NodeId of an AAS V2 asset from its identifier and opens a generated proxy.
        /// </summary>
        public AASAssetTypeClient OpenAsset(string id)
        {
            return new AASAssetTypeClient(Session, CreateAssetNodeId(id), Telemetry);
        }

        /// <summary>
        /// Computes the NodeId of an AAS V2 submodel from its identifier and opens a generated proxy.
        /// </summary>
        public AASSubmodelTypeClient OpenSubmodel(string id)
        {
            return new AASSubmodelTypeClient(Session, CreateSubmodelNodeId(id), Telemetry);
        }

        /// <summary>
        /// Computes the NodeId of an AAS V2 submodel element from the owning identifier and <c>idShortPath</c>.
        /// </summary>
        public AASSubmodelElementTypeClient OpenSubmodelElement(string ownerId, string idShortPath)
        {
            return new AASSubmodelElementTypeClient(
                Session,
                CreateSubmodelElementNodeId(ownerId, idShortPath),
                Telemetry);
        }

        /// <summary>
        /// Computes the NodeId of an AAS V2 property element and opens a generated proxy.
        /// </summary>
        public AASPropertyTypeClient OpenProperty(string ownerId, string idShortPath)
        {
            return new AASPropertyTypeClient(Session, CreateSubmodelElementNodeId(ownerId, idShortPath), Telemetry);
        }

        /// <summary>
        /// Computes the NodeId of an AAS V2 operation element and opens a generated proxy.
        /// </summary>
        public AASOperationTypeClient OpenOperation(string ownerId, string idShortPath)
        {
            return new AASOperationTypeClient(Session, CreateSubmodelElementNodeId(ownerId, idShortPath), Telemetry);
        }

        /// <summary>
        /// Computes the NodeId of an AAS V2 file element and opens a generated proxy.
        /// </summary>
        public AASFileTypeClient OpenFile(string ownerId, string idShortPath)
        {
            return new AASFileTypeClient(Session, CreateSubmodelElementNodeId(ownerId, idShortPath), Telemetry);
        }

        /// <summary>
        /// Computes the NodeId of an AAS V2 blob element and opens a generated proxy.
        /// </summary>
        public AASBlobTypeClient OpenBlob(string ownerId, string idShortPath)
        {
            return new AASBlobTypeClient(Session, CreateSubmodelElementNodeId(ownerId, idShortPath), Telemetry);
        }

        /// <summary>
        /// Creates a shell NodeId from an AAS V2 identifier.
        /// </summary>
        public NodeId CreateShellNodeId(string id)
        {
            return CreateIdentifiableNodeId(AasNodeKind.Shell, id);
        }

        /// <summary>
        /// Creates an asset NodeId from an AAS V2 identifier.
        /// </summary>
        public NodeId CreateAssetNodeId(string id)
        {
            return CreateSpecialV2IdentifiableNodeId("V2Asset", id);
        }

        /// <summary>
        /// Creates a submodel NodeId from an AAS V2 identifier.
        /// </summary>
        public NodeId CreateSubmodelNodeId(string id)
        {
            return CreateIdentifiableNodeId(AasNodeKind.Submodel, id);
        }

        /// <summary>
        /// Creates a submodel element NodeId from an owner identifier and <c>idShortPath</c>.
        /// </summary>
        public NodeId CreateSubmodelElementNodeId(string ownerId, string idShortPath)
        {
            return new NodeId(AasNodeIdEncoding.CreateElementId(ownerId, idShortPath), InstanceNamespaceIndex);
        }

        /// <summary>
        /// Browses an AAS V2 environment folder for top-level identifiables.
        /// </summary>
        public ValueTask<ArrayOf<AasBrowseEntry>> BrowseEnvironmentAsync(
            NodeId environmentNodeId,
            CancellationToken ct = default)
        {
            return BrowseChildrenAsync(
                environmentNodeId,
                ReferenceTypeIds.Organizes,
                includeSubtypes: true,
                nodeClassMask: (uint)NodeClass.Object,
                ct);
        }

        /// <summary>
        /// Browses a submodel for its direct submodel elements.
        /// </summary>
        public ValueTask<ArrayOf<AasBrowseEntry>> BrowseSubmodelElementsAsync(
            NodeId submodelNodeId,
            CancellationToken ct = default)
        {
            return BrowseChildrenAsync(
                submodelNodeId,
                ReferenceTypeIds.Aggregates,
                includeSubtypes: true,
                nodeClassMask: (uint)NodeClass.Object,
                ct);
        }

        /// <summary>
        /// Reads an element's <c>Value</c> Variable and its declared AAS V2 <c>ValueType</c> Property.
        /// </summary>
        public async ValueTask<AasValueReadResult> ReadValueAsync(NodeId elementNodeId, CancellationToken ct = default)
        {
            NodeId valueNodeId = await ResolveChildAsync(elementNodeId, "Value", ct).ConfigureAwait(false);
            NodeId valueTypeNodeId = await ResolveChildAsync(elementNodeId, "ValueType", ct).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadAttributesAsync(
                valueNodeId,
                valueTypeNodeId,
                Attributes.Value,
                ct).ConfigureAwait(false);

            ThrowIfBad(values[0].StatusCode, "Reading the AAS V2 Value failed.");
            ThrowIfBad(values[1].StatusCode, "Reading the AAS V2 ValueType failed.");

            AASValueTypeDataType valueType = ReadValueType(values[1].WrappedValue);
            return new AasValueReadResult(elementNodeId, valueNodeId, valueTypeNodeId, valueType, values[0].WrappedValue);
        }

        /// <summary>
        /// Writes an element's <c>Value</c> Variable after verifying the declared AAS V2 value type.
        /// </summary>
        public async ValueTask<StatusCode> WriteValueAsync(
            NodeId elementNodeId,
            AASValueTypeDataType valueType,
            Variant value,
            CancellationToken ct = default)
        {
            AasValueReadResult current = await ReadValueAsync(elementNodeId, ct).ConfigureAwait(false);
            if (current.ValueType != valueType)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    "The requested AAS V2 value type does not match the element's declared ValueType.");
            }

            if (!AasV2ValueTypeMap.IsCompatible(value, valueType))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    "The OPC UA value is not compatible with the declared AAS V2 ValueType.");
            }

            return await WriteRawValueAsync(current.ValueNodeId, value, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Invokes the embedded <c>Operation</c> Method on an AAS V2 operation element.
        /// </summary>
        public async ValueTask<AasOperationInvokeResult> InvokeAsync(
            NodeId operationNodeId,
            CancellationToken ct = default)
        {
            NodeId methodNodeId = await ResolveChildAsync(operationNodeId, "Operation", ct).ConfigureAwait(false);
            var request = new CallMethodRequest
            {
                ObjectId = operationNodeId,
                MethodId = methodNodeId,
                InputArguments = ArrayOf<Variant>.Empty
            };

            CallResponse response = await Session.CallAsync(
                requestHeader: null,
                methodsToCall: new[] { request }.ToArrayOf(),
                ct: ct).ConfigureAwait(false);
            if (response.Results.Count == 0)
            {
                return new AasOperationInvokeResult(
                    StatusCodes.BadUnexpectedError,
                    ArrayOf<Variant>.Empty,
                    false,
                    "The server returned no Call result.");
            }

            CallMethodResult result = response.Results[0];
            bool success = StatusCode.IsGood(result.StatusCode);
            return new AasOperationInvokeResult(
                result.StatusCode,
                result.OutputArguments,
                success,
                success ? string.Empty : result.StatusCode.ToString());
        }

        /// <summary>
        /// Reads all content from the embedded standard <see cref="FileTypeClient"/> under a File or Blob element.
        /// </summary>
        public async ValueTask<ByteString> ReadFileContentAsync(
            NodeId elementNodeId,
            int chunkSize = DefaultFileChunkSize,
            CancellationToken ct = default)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
            }

            NodeId fileNodeId = await ResolveChildAsync(elementNodeId, "File", ct).ConfigureAwait(false);
            var file = new FileTypeClient(Session, fileNodeId, Telemetry);
            const byte readMode = 1;
            uint handle = await file.OpenAsync(readMode, ct).ConfigureAwait(false);
            try
            {
                using MemoryStream buffer = new();
                while (true)
                {
                    ByteString chunk = await file.ReadAsync(handle, chunkSize, ct).ConfigureAwait(false);
                    if (chunk.IsNull || chunk.Span.Length == 0)
                    {
                        break;
                    }

                    byte[] bytes = chunk.Span.ToArray();
                    buffer.Write(bytes, 0, bytes.Length);
                    if (chunk.Span.Length < chunkSize)
                    {
                        break;
                    }
                }

                return ByteString.From(buffer.ToArray());
            }
            finally
            {
                await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private NodeId CreateIdentifiableNodeId(AasNodeKind kind, string id)
        {
            return new NodeId(AasNodeIdEncoding.CreateIdentifiableId(kind, id), InstanceNamespaceIndex);
        }

        private NodeId CreateSpecialV2IdentifiableNodeId(string discriminator, string id)
        {
            string escaped = AasNodeIdEncoding.Escape(id);
            string identifier = AasNodeIdEncoding.Prefix + discriminator + ":" +
                escaped.Length.ToString(CultureInfo.InvariantCulture) + ":" + escaped;
            return new NodeId(identifier, InstanceNamespaceIndex);
        }

        private async ValueTask<ArrayOf<AasBrowseEntry>> BrowseChildrenAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            CancellationToken ct)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("A NodeId is required.", nameof(nodeId));
            }

            var entries = new List<AasBrowseEntry>();
            ByteString continuationPoint = default;
            do
            {
                ArrayOf<ReferenceDescription> references;
                if (continuationPoint.IsNull)
                {
                    (_, continuationPoint, references) = await Session.BrowseAsync(
                        requestHeader: null,
                        view: null,
                        nodeId,
                        maxResultsToReturn: 0,
                        BrowseDirection.Forward,
                        referenceTypeId,
                        includeSubtypes,
                        nodeClassMask,
                        ct).ConfigureAwait(false);
                }
                else
                {
                    (_, continuationPoint, references) = await Session.BrowseNextAsync(
                        requestHeader: null,
                        releaseContinuationPoint: false,
                        continuationPoint,
                        ct).ConfigureAwait(false);
                }

                for (int i = 0; i < references.Count; i++)
                {
                    ReferenceDescription reference = references[i];
                    NodeId targetId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                    entries.Add(new AasBrowseEntry(targetId, reference.BrowseName, reference.DisplayName));
                }
            }
            while (!continuationPoint.IsNull);

            return entries.ToArrayOf();
        }

        private async ValueTask<NodeId> ResolveChildAsync(NodeId parentNodeId, string browseName, CancellationToken ct)
        {
            ArrayOf<AasBrowseEntry> children = await BrowseChildrenAsync(
                parentNodeId,
                ReferenceTypeIds.Aggregates,
                includeSubtypes: true,
                nodeClassMask: 0,
                ct).ConfigureAwait(false);
            for (int i = 0; i < children.Count; i++)
            {
                if (string.Equals(children[i].BrowseName.Name, browseName, StringComparison.Ordinal))
                {
                    return children[i].NodeId;
                }
            }

            throw new ServiceResultException(
                StatusCodes.BadNoMatch,
                string.Format(CultureInfo.InvariantCulture, "Child '{0}' was not found.", browseName));
        }

        private async ValueTask<ArrayOf<DataValue>> ReadAttributesAsync(
            NodeId firstNodeId,
            NodeId secondNodeId,
            uint attributeId,
            CancellationToken ct)
        {
            var nodesToRead = new ReadValueId[]
            {
                new() { NodeId = firstNodeId, AttributeId = attributeId },
                new() { NodeId = secondNodeId, AttributeId = attributeId }
            }.ToArrayOf();

            ReadResponse response = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: nodesToRead,
                ct: ct).ConfigureAwait(false);
            return response.Results;
        }

        private async ValueTask<StatusCode> WriteRawValueAsync(NodeId valueNodeId, Variant value, CancellationToken ct)
        {
            var nodesToWrite = new WriteValue[]
            {
                new()
                {
                    NodeId = valueNodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(value)
                }
            }.ToArrayOf();

            WriteResponse response = await Session.WriteAsync(
                requestHeader: null,
                nodesToWrite: nodesToWrite,
                ct: ct).ConfigureAwait(false);
            return response.Results.Count == 0 ? StatusCodes.BadUnexpectedError : response.Results[0];
        }

        private static AASValueTypeDataType ReadValueType(in Variant value)
        {
            if (value.TryGetValue(out int intValue) && Enum.IsDefined(typeof(AASValueTypeDataType), intValue))
            {
                return (AASValueTypeDataType)intValue;
            }

            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch,
                "The ValueType Property is not a valid AAS V2 ValueType enumeration value.");
        }

        private static void ThrowIfBad(StatusCode statusCode, string message)
        {
            if (StatusCode.IsBad(statusCode))
            {
                throw new ServiceResultException(statusCode, message);
            }
        }
    }
}
