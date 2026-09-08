/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: serializes a <see cref="NodeState"/> to a portable, self-describing
    /// payload and reconstructs it. The payload is framed as a 4-byte
    /// little-endian <see cref="NodeClass"/> followed by the standard
    /// <c>NodeState.SaveAsBinary</c> encoding, so a replica can reconstruct
    /// a generic node of the correct class without knowing the original
    /// concrete (possibly source-generated) type.
    /// </summary>
    /// <remarks>
    /// Reconstruction yields the matching generic base state
    /// (<see cref="BaseObjectState"/>, <see cref="BaseDataVariableState"/>,
    /// …). Type-specific behavior (method handlers, custom callbacks) is not
    /// carried in the payload — it is re-attached by the owning node manager
    /// on the active replica. This is sufficient for browse / read / value
    /// replication and active/passive failover.
    /// </remarks>
    public static class NodeStateSerializer
    {
        /// <summary>
        /// Serializes a node (and its children/references) to a framed
        /// binary payload.
        /// </summary>
        /// <param name="context">The system context for encoding.</param>
        /// <param name="node">The node to serialize.</param>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public static ByteString Serialize(ISystemContext context, NodeState node)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            using var stream = new MemoryStream();
            byte[] header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, (int)node.NodeClass);
            stream.Write(header, 0, 4);
            node.SaveAsBinary(context, stream);
            return new ByteString(stream.ToArray());
        }

        /// <summary>
        /// Reconstructs a node from a payload produced by
        /// <see cref="Serialize"/>.
        /// </summary>
        /// <param name="context">The system context for decoding.</param>
        /// <param name="payload">The framed binary payload.</param>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public static NodeState Deserialize(ISystemContext context, ByteString payload)
        {
            return DeserializePayload(context, payload);
        }

        internal static NodeState UpdateExisting(
            ISystemContext context,
            NodeState existingNode,
            NodeState source)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (existingNode == null)
            {
                throw new ArgumentNullException(nameof(existingNode));
            }
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (existingNode.NodeClass != source.NodeClass)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeClassInvalid,
                    $"Cannot update a {existingNode.NodeClass} node from a {source.NodeClass} payload.");
            }

            ExistingNodeSnapshot existing = CaptureExistingNode(context, existingNode);
            ApplyRuntimeState(context, existing, source);
            UpdateExistingNode(context, existing, source);
            return existingNode;
        }

        private static NodeState DeserializePayload(ISystemContext context, ByteString payload)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            byte[] bytes = payload.ToArray();
            if (bytes.Length < 4)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Distributed node payload is too short to contain a node class header.");
            }

            var nodeClass = (NodeClass)BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4));
            NodeState node = Create(nodeClass);
            using var stream = new MemoryStream(bytes, 4, bytes.Length - 4, writable: false);
            node.LoadAsBinary(context, stream);
            return node;
        }

        private static void UpdateExistingNode(
            ISystemContext context,
            ExistingNodeSnapshot existing,
            NodeState source)
        {
            var sourceChildren = new List<BaseInstanceState>();
            source.GetChildren(context, sourceChildren);

            DetachExistingChildren(existing);
            existing.Node.UpdateFrom(context, source);
            CopySerializedAttributes(existing.Node, source);
            existing.Node.Handle = existing.Handle;

            foreach (BaseInstanceState sourceChild in sourceChildren)
            {
                ExistingNodeSnapshot? existingChild = FindExistingChild(
                    existing,
                    sourceChild);
                if (existingChild == null)
                {
                    continue;
                }

                UpdateExistingNode(context, existingChild, sourceChild);
                var child = (BaseInstanceState)existingChild.Node;
                child.Parent = existing.Node;
                ReplaceChild(context, existing.Node, child);
            }
        }

        private static void ReplaceChild(
            ISystemContext context,
            NodeState parent,
            BaseInstanceState child)
        {
            if (parent is MethodState method &&
                child is PropertyState<ArrayOf<Argument>> arguments)
            {
                if (child.BrowseName.Name == BrowseNames.InputArguments)
                {
                    method.InputArguments = arguments;
                    return;
                }
                if (child.BrowseName.Name == BrowseNames.OutputArguments)
                {
                    method.OutputArguments = arguments;
                    return;
                }
            }
            if (parent is BaseDataVariableState variable &&
                child is PropertyState<ArrayOf<LocalizedText>> enumStrings &&
                child.BrowseName.Name == BrowseNames.EnumStrings)
            {
                variable.EnumStrings = enumStrings;
                return;
            }
            parent.ReplaceChild(context, child);
        }

        private static void DetachExistingChildren(ExistingNodeSnapshot existing)
        {
            if (existing.Node is MethodState method)
            {
                method.InputArguments = null;
                method.OutputArguments = null;
            }
            if (existing.Node is BaseDataVariableState variable)
            {
                variable.EnumStrings = null;
            }
            foreach (ExistingNodeSnapshot child in existing.Children)
            {
                existing.Node.RemoveChild((BaseInstanceState)child.Node);
            }
        }

        private static void ApplyRuntimeState(
            ISystemContext context,
            ExistingNodeSnapshot existing,
            NodeState source)
        {
            source.Handle = existing.Handle;
            if (existing.Node is BaseVariableState existingVariable &&
                source is BaseVariableState sourceVariable)
            {
                sourceVariable.Timestamp = existingVariable.Timestamp;
            }
            var sourceChildren = new List<BaseInstanceState>();
            source.GetChildren(context, sourceChildren);
            foreach (BaseInstanceState sourceChild in sourceChildren)
            {
                ExistingNodeSnapshot? existingChild = FindExistingChild(
                    existing,
                    sourceChild);
                if (existingChild != null)
                {
                    ApplyRuntimeState(context, existingChild, sourceChild);
                }
            }
        }

        private static ExistingNodeSnapshot? FindExistingChild(
            ExistingNodeSnapshot existing,
            BaseInstanceState sourceChild)
        {
            foreach (ExistingNodeSnapshot candidate in existing.Children)
            {
                if (candidate.NodeClass != sourceChild.NodeClass)
                {
                    continue;
                }
                if (!sourceChild.NodeId.IsNull &&
                    candidate.NodeId == sourceChild.NodeId)
                {
                    return candidate;
                }
            }
            foreach (ExistingNodeSnapshot candidate in existing.Children)
            {
                if (candidate.NodeClass == sourceChild.NodeClass &&
                    (candidate.NodeId.IsNull || sourceChild.NodeId.IsNull) &&
                    candidate.BrowseName == sourceChild.BrowseName)
                {
                    return candidate;
                }
            }
            return null;
        }

        private static ExistingNodeSnapshot CaptureExistingNode(
            ISystemContext context,
            NodeState node)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            var snapshots = new List<ExistingNodeSnapshot>(children.Count);
            foreach (BaseInstanceState child in children)
            {
                snapshots.Add(CaptureExistingNode(context, child));
            }
            return new ExistingNodeSnapshot(
                node,
                node.NodeId,
                node.BrowseName,
                node.NodeClass,
                node.Handle,
                snapshots);
        }

        private static void CopySerializedAttributes(NodeState target, NodeState source)
        {
            target.SymbolicName = source.SymbolicName;
            target.NodeId = source.NodeId;
            target.BrowseName = source.BrowseName;
            target.DisplayName = source.DisplayName;
            target.Description = source.Description;
            target.WriteMask = source.WriteMask;
            target.UserWriteMask = source.UserWriteMask;

            if (target is BaseInstanceState targetInstance &&
                source is BaseInstanceState sourceInstance)
            {
                targetInstance.ReferenceTypeId = sourceInstance.ReferenceTypeId;
                targetInstance.TypeDefinitionId = sourceInstance.TypeDefinitionId;
                targetInstance.ModellingRuleId = sourceInstance.ModellingRuleId;
                targetInstance.NumericId = sourceInstance.NumericId;
            }
            if (target is BaseObjectState targetObject &&
                source is BaseObjectState sourceObject)
            {
                targetObject.EventNotifier = sourceObject.EventNotifier;
            }
            if (target is BaseVariableState targetVariable &&
                source is BaseVariableState sourceVariable)
            {
                targetVariable.Value = sourceVariable.Value;
                targetVariable.StatusCode = sourceVariable.StatusCode;
                targetVariable.DataType = sourceVariable.DataType;
                targetVariable.ValueRank = sourceVariable.ValueRank;
                targetVariable.ArrayDimensions = sourceVariable.ArrayDimensions;
                targetVariable.AccessLevel = sourceVariable.AccessLevel;
                targetVariable.UserAccessLevel = sourceVariable.UserAccessLevel;
                targetVariable.MinimumSamplingInterval = sourceVariable.MinimumSamplingInterval;
                targetVariable.Historizing = sourceVariable.Historizing;
            }
            if (target is MethodState targetMethod &&
                source is MethodState sourceMethod)
            {
                targetMethod.Executable = sourceMethod.Executable;
                targetMethod.UserExecutable = sourceMethod.UserExecutable;
            }
            if (target is ViewState targetView &&
                source is ViewState sourceView)
            {
                targetView.EventNotifier = sourceView.EventNotifier;
                targetView.ContainsNoLoops = sourceView.ContainsNoLoops;
            }
            if (target is BaseTypeState targetType &&
                source is BaseTypeState sourceType)
            {
                targetType.SuperTypeId = sourceType.SuperTypeId;
                targetType.IsAbstract = sourceType.IsAbstract;
            }
            if (target is BaseVariableTypeState targetVariableType &&
                source is BaseVariableTypeState sourceVariableType)
            {
                targetVariableType.Value = sourceVariableType.Value;
                targetVariableType.DataType = sourceVariableType.DataType;
                targetVariableType.ValueRank = sourceVariableType.ValueRank;
                targetVariableType.ArrayDimensions = sourceVariableType.ArrayDimensions;
            }
            if (target is ReferenceTypeState targetReferenceType &&
                source is ReferenceTypeState sourceReferenceType)
            {
                targetReferenceType.InverseName = sourceReferenceType.InverseName;
                targetReferenceType.Symmetric = sourceReferenceType.Symmetric;
            }
            if (target is DataTypeState targetDataType &&
                source is DataTypeState sourceDataType)
            {
                targetDataType.DataTypeDefinition = sourceDataType.DataTypeDefinition;
            }
        }

        private static NodeState Create(NodeClass nodeClass)
        {
            return nodeClass switch
            {
                NodeClass.Object => new BaseObjectState(null),
                NodeClass.Variable => new BaseDataVariableState(null),
                NodeClass.Method => new MethodState(null),
                NodeClass.View => new ViewState(),
                NodeClass.ObjectType => new BaseObjectTypeState(),
                NodeClass.VariableType => new BaseDataVariableTypeState(),
                NodeClass.ReferenceType => new ReferenceTypeState(),
                NodeClass.DataType => new DataTypeState(),
                _ => throw new ServiceResultException(
                    StatusCodes.BadNodeClassInvalid,
                    $"Cannot reconstruct a node of class {nodeClass}.")
            };
        }

        private sealed class ExistingNodeSnapshot
        {
            public ExistingNodeSnapshot(
                NodeState node,
                NodeId nodeId,
                QualifiedName browseName,
                NodeClass nodeClass,
                object? handle,
                List<ExistingNodeSnapshot> children)
            {
                Node = node;
                NodeId = nodeId;
                BrowseName = browseName;
                NodeClass = nodeClass;
                Handle = handle;
                Children = children;
            }

            public NodeState Node { get; }

            public NodeId NodeId { get; }

            public QualifiedName BrowseName { get; }

            public NodeClass NodeClass { get; }

            public object? Handle { get; }

            public List<ExistingNodeSnapshot> Children { get; }
        }
    }
}
