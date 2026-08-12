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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.AI.Client
{
    internal sealed class AIClientOperations
    {
        public const int DefaultChunkSize = 4096;

        public AIClientOperations(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            RegisterEncodeableTypes(session);
        }

        public ISession Session { get; }

        public ITelemetryContext Telemetry { get; }

        public bool TryGetAINamespaceIndex(out ushort namespaceIndex)
        {
            int index = Session.NamespaceUris.GetIndex(Namespaces.AI);
            if (index < 0)
            {
                namespaceIndex = 0;
                return false;
            }
            namespaceIndex = (ushort)index;
            return true;
        }

        public NodeId AINamespaceType(uint identifier)
        {
            return TryGetAINamespaceIndex(out ushort ns)
                ? new NodeId(identifier, ns)
                : NodeId.Null;
        }

        public async ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            BrowseDirection direction,
            uint nodeClassMask,
            CancellationToken cancellationToken)
        {
            if (nodeId.IsNull)
            {
                return ArrayOf<ReferenceDescription>.Empty;
            }
            (ArrayOf<ArrayOf<ReferenceDescription>> results, ArrayOf<ServiceResult> errors) =
                await Session.ManagedBrowseAsync(
                    requestHeader: null,
                    view: null,
                    nodesToBrowse: [nodeId],
                    maxResultsToReturn: 0,
                    browseDirection: direction,
                    referenceTypeId: referenceTypeId,
                    includeSubtypes: true,
                    nodeClassMask: nodeClassMask,
                    ct: cancellationToken).ConfigureAwait(false);
            if (errors.Count > 0 && ServiceResult.IsBad(errors[0]))
            {
                return ArrayOf<ReferenceDescription>.Empty;
            }
            return results.Count > 0 ? results[0] : ArrayOf<ReferenceDescription>.Empty;
        }

        public ValueTask<ArrayOf<ReferenceDescription>> BrowseHierarchicalObjectsAsync(
            NodeId nodeId, CancellationToken cancellationToken)
        {
            return BrowseAsync(
                nodeId,
                global::Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                BrowseDirection.Forward,
                (uint)NodeClass.Object,
                cancellationToken);
        }

        public async ValueTask<ArrayOf<NodeId>> DiscoverInstancesAsync(
            NodeId root, NodeId typeDefinition, CancellationToken cancellationToken)
        {
            if (root.IsNull || typeDefinition.IsNull)
            {
                return ArrayOf<NodeId>.Empty;
            }
            ArrayOf<ReferenceDescription> references = await BrowseHierarchicalObjectsAsync(
                root, cancellationToken).ConfigureAwait(false);
            var matches = new List<NodeId>();
            for (int ii = 0; ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];
                NodeId typeDef = ExpandedNodeId.ToNodeId(
                    reference.TypeDefinition, Session.NamespaceUris);
                NodeId child = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                if (typeDef.IsNull || child.IsNull)
                {
                    continue;
                }
                if (typeDef == typeDefinition ||
                    await Session.NodeCache.IsTypeOfAsync(
                        typeDef, typeDefinition, cancellationToken).ConfigureAwait(false))
                {
                    matches.Add(child);
                }
            }
            return matches.ToArrayOf();
        }

        public async ValueTask<ArrayOf<NodeId>> ResolveChildrenAsync(
            NodeId parent,
            ArrayOf<string> browseNames,
            CancellationToken cancellationToken)
        {
            if (!TryGetAINamespaceIndex(out ushort ns))
            {
                var nulls = new List<NodeId>(browseNames.Count);
                for (int ii = 0; ii < browseNames.Count; ii++)
                {
                    nulls.Add(NodeId.Null);
                }
                return nulls.ToArrayOf();
            }
            return await ResolveChildrenAsync(parent, browseNames, ns, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<ArrayOf<NodeId>> ResolveChildrenAsync(
            NodeId parent,
            ArrayOf<string> browseNames,
            ushort namespaceIndex,
            CancellationToken cancellationToken)
        {
            if (parent.IsNull)
            {
                return CreateNullNodes(browseNames.Count);
            }
            var paths = new List<BrowsePath>(browseNames.Count);
            for (int ii = 0; ii < browseNames.Count; ii++)
            {
                paths.Add(CreateBrowsePath(parent, browseNames[ii], namespaceIndex));
            }
            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null, paths.ToArrayOf(), cancellationToken).ConfigureAwait(false);
            var results = new List<NodeId>(browseNames.Count);
            for (int ii = 0; ii < response.Results.Count; ii++)
            {
                BrowsePathResult result = response.Results[ii];
                results.Add(StatusCode.IsGood(result.StatusCode) && result.Targets.Count > 0
                    ? ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris)
                    : NodeId.Null);
            }
            while (results.Count < browseNames.Count)
            {
                results.Add(NodeId.Null);
            }
            return results.ToArrayOf();
        }

        public async ValueTask<NodeId> ResolveChildAsync(
            NodeId parent,
            string browseName,
            CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> nodes = await ResolveChildrenAsync(
                parent, [browseName], cancellationToken).ConfigureAwait(false);
            return nodes.Count > 0 ? nodes[0] : NodeId.Null;
        }

        public async ValueTask<NodeId> FollowReferenceAsync(
            NodeId source,
            uint referenceTypeIdentifier,
            CancellationToken cancellationToken)
        {
            NodeId referenceType = AINamespaceType(referenceTypeIdentifier);
            if (source.IsNull || referenceType.IsNull)
            {
                return NodeId.Null;
            }
            ArrayOf<ReferenceDescription> references = await BrowseAsync(
                source,
                referenceType,
                BrowseDirection.Forward,
                0,
                cancellationToken).ConfigureAwait(false);
            return references.Count > 0
                ? ExpandedNodeId.ToNodeId(references[0].NodeId, Session.NamespaceUris)
                : NodeId.Null;
        }

        public async ValueTask<DataValue> ReadValueAsync(
            NodeId nodeId, CancellationToken cancellationToken)
        {
            if (nodeId.IsNull)
            {
                return DataValue.Null;
            }
            return await Session.ReadValueAsync(nodeId, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<ArrayOf<DataValue>> ReadValuesAsync(
            ArrayOf<NodeId> nodeIds, CancellationToken cancellationToken)
        {
            var reads = new List<ReadValueId>(nodeIds.Count);
            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                if (!nodeIds[ii].IsNull)
                {
                    reads.Add(new ReadValueId
                    {
                        NodeId = nodeIds[ii],
                        AttributeId = Attributes.Value
                    });
                }
            }
            if (reads.Count == 0)
            {
                return ArrayOf<DataValue>.Empty;
            }
            ArrayOf<ReadValueId> nodesToRead = reads.ToArrayOf();
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both, nodesToRead, cancellationToken)
                .ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, nodesToRead);
            return response.Results;
        }

        public async ValueTask<ArrayOf<Variant>> CallAsync(
            NodeId objectId,
            string methodBrowseName,
            ArrayOf<Variant> inputArguments,
            CancellationToken cancellationToken)
        {
            NodeId methodId = await ResolveChildAsync(
                objectId, methodBrowseName, cancellationToken).ConfigureAwait(false);
            if (methodId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadMethodInvalid);
            }
            var request = new CallMethodRequest
            {
                ObjectId = objectId,
                MethodId = methodId,
                InputArguments = inputArguments
            };
            CallResponse response = await Session.CallAsync(null, [request], cancellationToken)
                .ConfigureAwait(false);
            CallMethodResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
            return result.OutputArguments;
        }

        public async ValueTask WriteFileAsync(
            FileTypeClient file,
            ByteString content,
            int chunkSize,
            CancellationToken cancellationToken)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            ValidateChunkSize(chunkSize);
            const byte writeEraseExisting = 6;
            uint handle = await file.OpenAsync(writeEraseExisting, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                ReadOnlyMemory<byte> bytes = content.IsNull
                    ? ReadOnlyMemory<byte>.Empty
                    : content.Span.ToArray();
                for (int offset = 0; offset < bytes.Length; offset += chunkSize)
                {
                    int take = Math.Min(chunkSize, bytes.Length - offset);
                    await file.WriteAsync(
                        handle,
                        ByteString.From(bytes.Slice(offset, take).ToArray()),
                        cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        public async ValueTask WriteFileAsync(
            NodeId file,
            ByteString content,
            int chunkSize,
            CancellationToken cancellationToken)
        {
            ValidateChunkSize(chunkSize);
            const byte writeEraseExisting = 6;
            ArrayOf<Variant> opened = await CallAsync(
                file, global::Opc.Ua.BrowseNames.Open, [Variant.From(writeEraseExisting)], cancellationToken)
                .ConfigureAwait(false);
            uint handle = opened.Count > 0 && opened[0].TryGetValue(out uint value) ? value : 0;
            try
            {
                ReadOnlyMemory<byte> bytes = content.IsNull
                    ? ReadOnlyMemory<byte>.Empty
                    : content.Span.ToArray();
                for (int offset = 0; offset < bytes.Length; offset += chunkSize)
                {
                    int take = Math.Min(chunkSize, bytes.Length - offset);
                    await CallAsync(
                        file,
                        global::Opc.Ua.BrowseNames.Write,
                        [
                            Variant.From(handle),
                            Variant.From(ByteString.From(bytes.Slice(offset, take).ToArray()))
                        ],
                        cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                await CallAsync(
                    file,
                    global::Opc.Ua.BrowseNames.Close,
                    [Variant.From(handle)],
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        public async ValueTask WriteFileAsync(
            FileTypeClient file,
            Stream content,
            int chunkSize,
            CancellationToken cancellationToken)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            if (content is null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            if (!content.CanRead)
            {
                throw new ArgumentException("Stream must be readable.", nameof(content));
            }
            ValidateChunkSize(chunkSize);
            const byte writeEraseExisting = 6;
            uint handle = await file.OpenAsync(writeEraseExisting, cancellationToken)
                .ConfigureAwait(false);
            byte[] buffer = new byte[chunkSize];
            try
            {
                while (true)
                {
                    int read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }
                    byte[] chunk = new byte[read];
                    Array.Copy(buffer, chunk, read);
                    await file.WriteAsync(handle, ByteString.From(chunk), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        public async ValueTask<ByteString> ReadFileAsync(
            FileTypeClient file,
            int chunkSize,
            CancellationToken cancellationToken)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            using MemoryStream stream = new();
            await ReadFileAsync(file, stream, chunkSize, cancellationToken).ConfigureAwait(false);
            return ByteString.From(stream.ToArray());
        }

        public async ValueTask<ByteString> ReadFileAsync(
            NodeId file,
            int chunkSize,
            CancellationToken cancellationToken)
        {
            ValidateChunkSize(chunkSize);
            const byte readMode = 1;
            ArrayOf<Variant> opened = await CallAsync(
                file, global::Opc.Ua.BrowseNames.Open, [Variant.From(readMode)], cancellationToken)
                .ConfigureAwait(false);
            uint handle = opened.Count > 0 && opened[0].TryGetValue(out uint value) ? value : 0;
            using MemoryStream buffer = new();
            try
            {
                while (true)
                {
                    ArrayOf<Variant> outputs = await CallAsync(
                        file,
                        global::Opc.Ua.BrowseNames.Read,
                        [Variant.From(handle), Variant.From(chunkSize)],
                        cancellationToken).ConfigureAwait(false);
                    if (outputs.Count == 0 ||
                        !outputs[0].TryGetValue(out ByteString chunk) ||
                        chunk.IsNull ||
                        chunk.Length == 0)
                    {
                        break;
                    }
                    byte[] copy = chunk.Span.ToArray();
                    await buffer.WriteAsync(copy.AsMemory(0, copy.Length), cancellationToken)
                        .ConfigureAwait(false);
                    if (copy.Length < chunkSize)
                    {
                        break;
                    }
                }
            }
            finally
            {
                await CallAsync(file, global::Opc.Ua.BrowseNames.Close, [Variant.From(handle)], CancellationToken.None)
                    .ConfigureAwait(false);
            }
            return ByteString.From(buffer.ToArray());
        }

        public async ValueTask ReadFileAsync(
            FileTypeClient file,
            Stream destination,
            int chunkSize,
            CancellationToken cancellationToken)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (!destination.CanWrite)
            {
                throw new ArgumentException("Stream must be writable.", nameof(destination));
            }
            ValidateChunkSize(chunkSize);
            const byte readMode = 1;
            uint handle = await file.OpenAsync(readMode, cancellationToken).ConfigureAwait(false);
            try
            {
                while (true)
                {
                    ByteString chunk = await file.ReadAsync(handle, chunkSize, cancellationToken)
                        .ConfigureAwait(false);
                    if (chunk.IsNull || chunk.Length == 0)
                    {
                        break;
                    }
                    byte[] copy = chunk.Span.ToArray();
                    await destination.WriteAsync(copy.AsMemory(0, copy.Length), cancellationToken)
                        .ConfigureAwait(false);
                    if (copy.Length < chunkSize)
                    {
                        break;
                    }
                }
            }
            finally
            {
                await file.CloseAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        public static string? ReadString(DataValue value)
        {
            return value.WrappedValue.TryGetValue(out string? text) ? text : null;
        }

        public static ByteString ReadByteString(DataValue value)
        {
            return value.WrappedValue.TryGetValue(out ByteString bytes) ? bytes : ByteString.Empty;
        }

        public static bool ReadBoolean(DataValue value)
        {
            return value.WrappedValue.TryGetValue(out bool result) && result;
        }

        public static ulong ReadUInt64(DataValue value)
        {
            return value.WrappedValue.TryGetValue(out ulong result) ? result : 0;
        }

        public static uint ReadUInt32(DataValue value)
        {
            return value.WrappedValue.TryGetValue(out uint result) ? result : 0;
        }

        public static double ReadDouble(DataValue value)
        {
            return value.WrappedValue.TryGetValue(out double result) ? result : 0;
        }

        public static DateTimeUtc ReadDateTime(DataValue value)
        {
            return value.WrappedValue.TryGetValue(out DateTimeUtc result) ? result : default;
        }

        public static bool TryReadEnum<TEnum>(DataValue value, out TEnum result)
            where TEnum : struct, Enum
        {
            if (value.WrappedValue.TryGetValue(out int intValue))
            {
                result = (TEnum)Enum.ToObject(typeof(TEnum), intValue);
                return true;
            }
            if (value.WrappedValue.TryGetValue(out uint uintValue))
            {
                result = (TEnum)Enum.ToObject(typeof(TEnum), uintValue);
                return true;
            }
            result = default;
            return false;
        }

        public static bool TryReadNodeId(DataValue value, out NodeId nodeId)
        {
            if (value.WrappedValue.TryGetValue(out NodeId candidate))
            {
                nodeId = candidate;
                return !nodeId.IsNull;
            }
            nodeId = NodeId.Null;
            return false;
        }

        private static ArrayOf<NodeId> CreateNullNodes(int count)
        {
            var nulls = new List<NodeId>(count);
            for (int ii = 0; ii < count; ii++)
            {
                nulls.Add(NodeId.Null);
            }
            return nulls.ToArrayOf();
        }

        private static BrowsePath CreateBrowsePath(
            NodeId parent, string browseName, ushort namespaceIndex)
        {
            return new BrowsePath
            {
                StartingNode = parent,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(browseName, namespaceIndex)
                        }
                    ]
                }
            };
        }

        private static void ValidateChunkSize(int chunkSize)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
            }
        }

        private static void RegisterEncodeableTypes(ISession session)
        {
            RegisterEncodeableTypes(session.Factory);
            if (!ReferenceEquals(session.MessageContext.Factory, session.Factory))
            {
                RegisterEncodeableTypes(session.MessageContext.Factory);
            }
        }

        private static void RegisterEncodeableTypes(IEncodeableFactory factory)
        {
            var probe = new CapabilityDataType();
            if (!factory.TryGetEncodeableType(probe.BinaryEncodingId, out _))
            {
                factory.Builder.AddOpcUaAI().Commit();
            }
        }
    }
}
