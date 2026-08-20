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
using Opc.Ua.Aas.Server.Assets;
using Opc.Ua.Aas.V2;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Aas.Server.V2
{
    /// <summary>
    /// Binds AAS V2 values, Operation methods and embedded FileType methods to providers.
    /// </summary>
    public sealed class AasV2EnvironmentRuntime : IAsyncDisposable
    {
        /// <summary>
        /// Initializes a runtime for one materialized AAS V2 environment.
        /// </summary>
        public AasV2EnvironmentRuntime(
            AasEnvironment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler)
        {
            m_environment = environment ?? throw new ArgumentNullException(nameof(environment));
            m_valueProvider = valueProvider ?? throw new ArgumentNullException(nameof(valueProvider));
            m_operationHandler = operationHandler ?? throw new ArgumentNullException(nameof(operationHandler));
            CollectEnvironment();
        }

        /// <summary>
        /// Configures callbacks on the runtime NodeSet builder.
        /// </summary>
        public ValueTask<IAsyncDisposable?> ConfigureAsync(
            INodeManagerBuilder builder,
            CancellationToken cancellationToken)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            ushort namespaceIndex = ResolveNamespaceIndex(builder.Context);
            foreach (NodeId operationNodeId in m_operations)
            {
                NodeId boundOperationNodeId = Rebase(operationNodeId, namespaceIndex);
                builder.Node(boundOperationNodeId).OnCall((context, method, objectId, inputArguments, outputArguments,
                    ct) => InvokeAsync(operationNodeId, inputArguments, outputArguments, ct));
            }
            foreach (NodeId valueNodeId in m_valueNodeIds)
            {
                NodeId boundNodeId = Rebase(valueNodeId, namespaceIndex);
                NodeId lookupNodeId = valueNodeId;
                builder.Node(boundNodeId)
                    .OnRead((context, node, range, encoding, ct) => ReadValueAsync(lookupNodeId, ct))
                    .OnWrite((context, node, range, value, ct) => WriteValueAsync(lookupNodeId, value, ct));
            }
            foreach (KeyValuePair<NodeId, RuntimeFile> kvp in m_files)
            {
                NodeId fileNodeId = Rebase(kvp.Key, namespaceIndex);
                RuntimeFile file = kvp.Value;
                builder.Node(MemberNodeId(fileNodeId, "Open")).OnCall((context, method, objectId, inputArguments,
                    outputArguments, ct) => OpenFileAsync(file, inputArguments, outputArguments));
                builder.Node(MemberNodeId(fileNodeId, "Read")).OnCall((context, method, objectId, inputArguments,
                    outputArguments, ct) => ReadFileAsync(file, inputArguments, outputArguments));
                builder.Node(MemberNodeId(fileNodeId, "Close")).OnCall((context, method, objectId, inputArguments,
                    outputArguments, ct) => CloseFileAsync(file, inputArguments, outputArguments));
                builder.Node(MemberNodeId(fileNodeId, "Write")).OnCall((context, method, objectId, inputArguments,
                    outputArguments, ct) => WriteFileAsync(file, inputArguments, outputArguments));
                builder.Node(MemberNodeId(fileNodeId, "GetPosition")).OnCall((context, method, objectId,
                    inputArguments, outputArguments, ct) =>
                        GetFilePositionAsync(file, inputArguments, outputArguments));
                builder.Node(MemberNodeId(fileNodeId, "SetPosition")).OnCall((context, method, objectId,
                    inputArguments, outputArguments, ct) =>
                        SetFilePositionAsync(file, inputArguments, outputArguments));
            }
            return new ValueTask<IAsyncDisposable?>((IAsyncDisposable?)this);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            foreach (RuntimeFile file in m_files.Values)
            {
                file.Dispose();
            }
            m_files.Clear();
            return new ValueTask();
        }

        private static ushort ResolveNamespaceIndex(ISystemContext context)
        {
            int index = context?.NamespaceUris?.GetIndex(Opc.Ua.Aas.V2.Namespaces.AasV2) ?? -1;
            return index < 0 ? AuthoredNamespaceIndex : (ushort)index;
        }

        private static NodeId Rebase(NodeId nodeId, ushort namespaceIndex)
        {
            if (nodeId.NamespaceIndex == namespaceIndex || !nodeId.TryGetValue(out string? identifier))
            {
                return nodeId;
            }
            return new NodeId(identifier, namespaceIndex);
        }

        private async ValueTask<ServiceResult> InvokeAsync(
            NodeId operationNodeId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            if (inputArguments.Count != 0)
            {
                return StatusCodes.BadInvalidArgument;
            }

            AasOperationInvokeResult result = await m_operationHandler.InvokeAsync(
                new AasOperationInvokeRequest(operationNodeId, [], [], 0d),
                cancellationToken).ConfigureAwait(false);
            if (result.OutputValues.Count != 0 || result.InoutputResults.Count != 0)
            {
                return StatusCodes.BadInvalidArgument;
            }
            return result.Success ? ServiceResult.Good : StatusCodes.BadInvalidState;
        }

        private async ValueTask<AttributeReadResult> ReadValueAsync(
            NodeId valueNodeId,
            CancellationToken cancellationToken)
        {
            AasValueReadResult result = await m_valueProvider.ReadValueAsync(
                valueNodeId,
                cancellationToken).ConfigureAwait(false);
            return new AttributeReadResult(
                result.Result,
                result.Value,
                result.StatusCode,
                result.SourceTimestamp);
        }

        private async ValueTask<AttributeWriteResult> WriteValueAsync(
            NodeId valueNodeId,
            Variant value,
            CancellationToken cancellationToken)
        {
            ServiceResult result = await m_valueProvider.WriteValueAsync(
                valueNodeId,
                value,
                cancellationToken).ConfigureAwait(false);
            return new AttributeWriteResult(result);
        }

        private static ValueTask<ServiceResult> OpenFileAsync(
            RuntimeFile file,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            if (inputArguments.Count != 1 || !inputArguments[0].TryGetValue(out byte mode))
            {
                return new ValueTask<ServiceResult>(StatusCodes.BadInvalidArgument);
            }
            uint handle = 0;
            ServiceResult result = file.State.Open!.OnCall!(null!, file.State.Open, file.State.NodeId, mode,
                ref handle);
            if (ServiceResult.IsGood(result))
            {
                outputArguments.Add(new Variant(handle));
            }
            return new ValueTask<ServiceResult>(result);
        }

        private static ValueTask<ServiceResult> ReadFileAsync(
            RuntimeFile file,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            if (inputArguments.Count != 2 ||
                !inputArguments[0].TryGetValue(out uint handle) ||
                !inputArguments[1].TryGetValue(out int length))
            {
                return new ValueTask<ServiceResult>(StatusCodes.BadInvalidArgument);
            }
            ByteString data = ByteString.Empty;
            ServiceResult result = file.State.Read!.OnCall!(null!, file.State.Read, file.State.NodeId, handle,
                length, ref data);
            if (ServiceResult.IsGood(result))
            {
                outputArguments.Add(new Variant(data));
            }
            return new ValueTask<ServiceResult>(result);
        }

        private static ValueTask<ServiceResult> CloseFileAsync(
            RuntimeFile file,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            if (inputArguments.Count != 1 || !inputArguments[0].TryGetValue(out uint handle))
            {
                return new ValueTask<ServiceResult>(StatusCodes.BadInvalidArgument);
            }
            return new ValueTask<ServiceResult>(file.State.Close!.OnCall!(null!, file.State.Close, file.State.NodeId,
                handle));
        }

        private static ValueTask<ServiceResult> WriteFileAsync(
            RuntimeFile file,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            if (inputArguments.Count != 2 ||
                !inputArguments[0].TryGetValue(out uint handle) ||
                !inputArguments[1].TryGetValue(out ByteString data))
            {
                return new ValueTask<ServiceResult>(StatusCodes.BadInvalidArgument);
            }
            return new ValueTask<ServiceResult>(file.State.Write!.OnCall!(null!, file.State.Write,
                file.State.NodeId, handle, data));
        }

        private static ValueTask<ServiceResult> GetFilePositionAsync(
            RuntimeFile file,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            if (inputArguments.Count != 1 || !inputArguments[0].TryGetValue(out uint handle))
            {
                return new ValueTask<ServiceResult>(StatusCodes.BadInvalidArgument);
            }
            ulong position = 0;
            ServiceResult result = file.State.GetPosition!.OnCall!(null!, file.State.GetPosition,
                file.State.NodeId, handle, ref position);
            if (ServiceResult.IsGood(result))
            {
                outputArguments.Add(new Variant(position));
            }
            return new ValueTask<ServiceResult>(result);
        }

        private static ValueTask<ServiceResult> SetFilePositionAsync(
            RuntimeFile file,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            if (inputArguments.Count != 2 ||
                !inputArguments[0].TryGetValue(out uint handle) ||
                !inputArguments[1].TryGetValue(out ulong position))
            {
                return new ValueTask<ServiceResult>(StatusCodes.BadInvalidArgument);
            }
            return new ValueTask<ServiceResult>(file.State.SetPosition!.OnCall!(null!, file.State.SetPosition,
                file.State.NodeId, handle, position));
        }

        private void CollectEnvironment()
        {
            if (m_environment.Submodels.IsPresent)
            {
                foreach (AasSubmodel submodel in m_environment.Submodels.Value.Span)
                {
                    CollectSubmodel(submodel.Identification.Id, submodel);
                }
            }
            if (m_environment.AssetAdministrationShells.IsPresent)
            {
                foreach (AasShell shell in m_environment.AssetAdministrationShells.Value.Span)
                {
                    if (shell.Submodels.IsPresent)
                    {
                        foreach (AasSubmodel submodel in shell.Submodels.Value.Span)
                        {
                            CollectSubmodel(shell.Identification.Id, submodel);
                        }
                    }
                }
            }
        }

        private void CollectSubmodel(string ownerId, AasSubmodel submodel)
        {
            CollectElements(ownerId, string.Empty, submodel.SubmodelElements, ordered: false);
        }

        private void CollectElements(
            string ownerId,
            string parentPath,
            AasOptional<ArrayOf<AasSubmodelElement>> elements,
            bool ordered)
        {
            if (!elements.IsPresent)
            {
                return;
            }

            for (int i = 0; i < elements.Value.Count; i++)
            {
                string path = ordered
                    ? AasIdShortPath.AppendIndex(parentPath, i)
                    : AasIdShortPath.AppendName(parentPath, elements.Value[i].IdShort);
                CollectElementAtPath(ownerId, path, elements.Value[i]);
            }
        }

        private void CollectElementAtPath(string ownerId, string path, AasSubmodelElement element)
        {
            var nodeId = new NodeId(AasNodeIdEncoding.CreateElementId(ownerId, path), AuthoredNamespaceIndex);
            switch (element)
            {
                case AasBlob blob:
                    CollectFile(nodeId, blob.File, string.Empty);
                    break;
                case AasEntity entity:
                    CollectElements(ownerId, path, entity.Statements, ordered: false);
                    break;
                case AasFile file:
                    m_valueNodeIds.Add(MemberNodeId(nodeId, "Value"));
                    CollectFile(nodeId, file.File, file.MimeType);
                    break;
                case AasMultiLanguageProperty or AasProperty:
                    m_valueNodeIds.Add(MemberNodeId(nodeId, "Value"));
                    break;
                case AasOperation:
                    m_operations.Add(MemberNodeId(nodeId, "Operation"));
                    break;
                case AasRange:
                    m_valueNodeIds.Add(MemberNodeId(nodeId, "Min"));
                    m_valueNodeIds.Add(MemberNodeId(nodeId, "Max"));
                    break;
                case AasAnnotatedRelationshipElement annotated:
                    CollectElements(ownerId, path, annotated.DataElements, ordered: false);
                    break;
                case AasOrderedSubmodelElementCollection orderedCollection:
                    CollectElements(ownerId, path, orderedCollection.SubmodelElements, ordered: true);
                    break;
                case AasSubmodelElementCollection collection:
                    CollectElements(ownerId, path, collection.SubmodelElements, ordered: false);
                    break;
            }
        }

        private void CollectFile(
            NodeId elementNodeId,
            AasOptional<AasFileObject> file,
            string contentType)
        {
            NodeId fileNodeId = MemberNodeId(elementNodeId, "File");

            // The materializer emits the FileType object unconditionally, because
            // OPC 30270 declares it Mandatory, but emits its Value Variable only
            // when content is actually carried. Binding a Variable the NodeSet
            // does not contain makes the builder throw BadNodeIdUnknown and
            // aborts the whole projection, so the two have to agree.
            if (file.IsPresent)
            {
                m_valueNodeIds.Add(MemberNodeId(fileNodeId, "Value"));
            }

            ByteString content = file.IsPresent && file.Value.Value.IsPresent
                ? file.Value.Value.Value
                : ByteString.Empty;
            m_files[fileNodeId] = new RuntimeFile(fileNodeId, content, contentType);
        }

        private static NodeId MemberNodeId(NodeId parentNodeId, string browseName)
        {
            if (!parentNodeId.TryGetValue(out string? identifier))
            {
                return NodeId.Null;
            }
            return new NodeId(identifier + "." + AasNodeIdEncoding.Escape(browseName), parentNodeId.NamespaceIndex);
        }

        private const ushort AuthoredNamespaceIndex = 1;

        private readonly AasEnvironment m_environment;
        private readonly IAasValueProvider m_valueProvider;
        private readonly IAasOperationHandler m_operationHandler;
        private readonly List<NodeId> m_operations = [];
        private readonly List<NodeId> m_valueNodeIds = [];
        private readonly Dictionary<NodeId, RuntimeFile> m_files = [];

        private sealed class RuntimeFile : IDisposable
        {
            public RuntimeFile(NodeId nodeId, ByteString content, string contentType)
            {
                State = CreateFileState(nodeId);
                Manager = new AasElementFileManager(State, content, contentType);
            }

            public FileState State { get; }

            private AasElementFileManager Manager { get; }

            public void Dispose()
            {
                Manager.Dispose();
            }

            private static FileState CreateFileState(NodeId nodeId)
            {
                var context = new SystemContext(telemetry: null!)
                {
                    NamespaceUris = new NamespaceTable(),
                    ServerUris = new StringTable()
                };
                var file = new FileState(null)
                {
                    NodeId = nodeId,
                    BrowseName = new QualifiedName("File", nodeId.NamespaceIndex),
                    DisplayName = new LocalizedText("File")
                };
                file.Create(context, file.NodeId, file.BrowseName, file.DisplayName, true);
                return file;
            }
        }
    }
}
