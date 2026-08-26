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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Aas.Server.Materialization
{
    internal sealed class AasEnvironmentRuntime : IAsyncDisposable
    {
        public AasEnvironmentRuntime(
            AasEnvironment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler)
        {
            m_environment = environment ?? throw new ArgumentNullException(nameof(environment));
            m_valueProvider = valueProvider ?? throw new ArgumentNullException(nameof(valueProvider));
            m_operationHandler = operationHandler ?? throw new ArgumentNullException(nameof(operationHandler));
            CollectOperations();
        }

        public ValueTask<IAsyncDisposable?> ConfigureAsync(
            INodeManagerBuilder builder,
            CancellationToken cancellationToken)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            // The materialized NodeSet declares the AAS namespace first, so its
            // nodes are collected at index one. A Server assigns that namespace
            // whatever index its own table gives it, so the collected ids are
            // rebased before anything is bound; binding index one would look up
            // nodes that do not exist.
            ushort namespaceIndex = ResolveNamespaceIndex(builder.Context);

            foreach (KeyValuePair<NodeId, AasOperationDescriptor> kvp in m_operations)
            {
                NodeId methodNodeId = MemberNodeId(Rebase(kvp.Key, namespaceIndex), "Invoke");
                AasOperationDescriptor descriptor = kvp.Value;
                NodeId operationNodeId = kvp.Key;
                builder.Node(methodNodeId).OnCall((context, method, objectId, inputArguments, outputArguments, ct) =>
                    InvokeAsync(operationNodeId, descriptor, inputArguments, outputArguments, ct));
            }

            foreach (NodeId valueNodeId in m_valueNodeIds)
            {
                NodeId boundNodeId = Rebase(valueNodeId, namespaceIndex);
                NodeId lookupNodeId = valueNodeId;
                builder.Node(boundNodeId)
                    .OnRead((context, node, range, encoding, ct) => ReadValueAsync(lookupNodeId, ct))
                    .OnWrite((context, node, range, value, ct) => WriteValueAsync(lookupNodeId, value, ct));
            }

            return new ValueTask<IAsyncDisposable?>((IAsyncDisposable?)this);
        }

        private static ushort ResolveNamespaceIndex(ISystemContext context)
        {
            int index = context?.NamespaceUris?.GetIndex(Opc.Ua.Aas.V3.Namespaces.AasV3) ?? -1;
            return index < 0 ? AuthoredNamespaceIndex : (ushort)index;
        }

        private static NodeId Rebase(NodeId nodeId, ushort namespaceIndex)
        {
            if (nodeId.NamespaceIndex == namespaceIndex ||
                !nodeId.TryGetValue(out string? identifier))
            {
                return nodeId;
            }
            return new NodeId(identifier, namespaceIndex);
        }

        public ValueTask DisposeAsync()
        {
            return new ValueTask();
        }

        private async ValueTask<ServiceResult> InvokeAsync(
            NodeId operationNodeId,
            AasOperationDescriptor descriptor,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            ArrayOf<Variant> inputValues = GetArrayArgument(inputArguments, 0);
            ArrayOf<Variant> inoutputValues = GetArrayArgument(inputArguments, 1);
            double clientTimeout = GetDurationArgument(inputArguments, 2);
            if (inputValues.Count != descriptor.InputCount ||
                inoutputValues.Count != descriptor.InoutputCount)
            {
                return StatusCodes.BadInvalidArgument;
            }

            AasOperationInvokeResult result = await m_operationHandler.InvokeAsync(
                new AasOperationInvokeRequest(operationNodeId, inputValues, inoutputValues, clientTimeout),
                cancellationToken).ConfigureAwait(false);
            if (result.OutputValues.Count != descriptor.OutputCount ||
                result.InoutputResults.Count != descriptor.InoutputCount)
            {
                return StatusCodes.BadInvalidArgument;
            }

            outputArguments.Add(new Variant(result.OutputValues));
            outputArguments.Add(new Variant(result.InoutputResults));
            outputArguments.Add(new Variant(result.Success));
            outputArguments.Add(new Variant(result.Diagnostic));
            return ServiceResult.Good;
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

        private void CollectOperations()
        {
            if (!m_environment.Submodels.IsPresent)
            {
                return;
            }

            foreach (AasSubmodel submodel in m_environment.Submodels.Value.Span)
            {
                if (submodel.SubmodelElements.IsPresent)
                {
                    CollectElements(submodel.Id, string.Empty, submodel.SubmodelElements.Value);
                }
            }
        }

        private void CollectElements(string ownerId, string parentPath, ArrayOf<AasSubmodelElement> elements)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                AasSubmodelElement element = elements[i];
                if (!element.IdShort.IsPresent)
                {
                    continue;
                }
                string path = string.IsNullOrEmpty(parentPath)
                    ? element.IdShort.Value
                    : parentPath + "." + element.IdShort.Value;
                CollectElementAtPath(ownerId, path, element);
            }
        }

        /// <summary>
        /// Collects one element that has already been placed at its path.
        /// </summary>
        private void CollectElementAtPath(string ownerId, string path, AasSubmodelElement element)
        {
            string elementId = AasNodeIdEncoding.CreateElementId(ownerId, path);
            NodeId nodeId = new(elementId, AuthoredNamespaceIndex);
            switch (element)
            {
                case AasOperation operation:
                    m_operations[nodeId] = new AasOperationDescriptor(
                        Count(operation.InputVariables),
                        Count(operation.OutputVariables),
                        Count(operation.InoutputVariables));
                    CollectOperationRole(ownerId, path, AasOperationVariableRole.Input, operation.InputVariables);
                    CollectOperationRole(ownerId, path, AasOperationVariableRole.Output, operation.OutputVariables);
                    CollectOperationRole(ownerId, path, AasOperationVariableRole.Inoutput, operation.InoutputVariables);
                    break;
                case AasSubmodelElementCollection collection when collection.Value.IsPresent:
                    CollectElements(ownerId, path, collection.Value.Value);
                    break;
                case AasSubmodelElementList list when list.Value.IsPresent:
                    CollectElements(ownerId, path, list.Value.Value);
                    break;
            }

            if (element is AasProperty or AasMultiLanguageProperty or AasBlob or AasFile or AasReferenceElement)
            {
                m_valueNodeIds.Add(MemberNodeId(nodeId, "Value"));
            }
        }

        private void CollectOperationRole(
            string ownerId,
            string path,
            AasOperationVariableRole role,
            AasOptional<ArrayOf<AasSubmodelElement>> variables)
        {
            if (!variables.IsPresent)
            {
                return;
            }

            for (int i = 0; i < variables.Value.Count; i++)
            {
                // The materializer places an operation variable at the role
                // path itself and gives the node the element's idShort only as
                // its BrowseName, so the idShort is not part of the path.
                CollectElementAtPath(
                    ownerId,
                    AasIdShortPath.AppendOperationVariable(path, role, i),
                    variables.Value[i]);
            }
        }

        private static int Count(AasOptional<ArrayOf<AasSubmodelElement>> values)
        {
            return values.IsPresent ? values.Value.Count : 0;
        }

        private static NodeId MemberNodeId(NodeId parentNodeId, string browseName)
        {
            if (!parentNodeId.TryGetValue(out string? identifier))
            {
                return NodeId.Null;
            }
            return new NodeId(identifier + "." + AasNodeIdEncoding.Escape(browseName), parentNodeId.NamespaceIndex);
        }

        private static ArrayOf<Variant> GetArrayArgument(ArrayOf<Variant> inputArguments, int index)
        {
            if (index >= inputArguments.Count)
            {
                return [];
            }
            if (inputArguments[index].AsBoxedObject(Variant.BoxingBehavior.Legacy) is ArrayOf<Variant> array)
            {
                return array;
            }
            if (inputArguments[index].AsBoxedObject(Variant.BoxingBehavior.Legacy) is Variant[] values)
            {
                return new ArrayOf<Variant>(values);
            }
            return [];
        }

        private static double GetDurationArgument(ArrayOf<Variant> inputArguments, int index)
        {
            if (index >= inputArguments.Count)
            {
                return 0;
            }
            object? value = inputArguments[index].AsBoxedObject(Variant.BoxingBehavior.Legacy);
            return value is double duration ? duration : 0;
        }

        /// <summary>
        /// The namespace index the materialized NodeSet authors its nodes at,
        /// which is one because the AAS namespace is declared first.
        /// </summary>
        private const ushort AuthoredNamespaceIndex = 1;

        private readonly AasEnvironment m_environment;
        private readonly IAasValueProvider m_valueProvider;
        private readonly IAasOperationHandler m_operationHandler;
        private readonly Dictionary<NodeId, AasOperationDescriptor> m_operations = [];
        private readonly List<NodeId> m_valueNodeIds = [];

        private readonly record struct AasOperationDescriptor(int InputCount, int OutputCount, int InoutputCount);
    }
}
