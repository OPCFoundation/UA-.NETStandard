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
using System.Collections.Generic;
using System.IO;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: serializes a <see cref="NodeState"/> to a portable, self-describing
    /// payload and reconstructs it. A versioned replication envelope contains the standard
    /// <c>NodeState.SaveAsBinary</c> encoding and an independently framed authorization
    /// policy for the node and every descendant. The global node binary format is unchanged.
    /// </summary>
    /// <remarks>
    /// Reconstruction yields the matching generic base state
    /// (<see cref="BaseObjectState"/>, <see cref="BaseDataVariableState"/>,
    /// …). Type-specific behavior (method handlers, custom callbacks) is not
    /// carried in the payload — it is re-attached by the owning node manager
    /// on the active replica. This is sufficient for browse / read / value
    /// replication and active/passive failover. Legacy payloads without an authorization
    /// envelope, unsupported versions and policies that cannot be reconstructed are rejected.
    /// Nodes with caller-dependent authorization callbacks cannot be represented by this envelope.
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
        /// <exception cref="ServiceResultException">The node's authorization cannot be represented safely.</exception>
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

            using var nodeStream = new MemoryStream();
            node.SaveAsBinary(context, nodeStream);
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(stream, context.AsMessageContext(), true);
            encoder.WriteUInt32(null, EnvelopeMagic);
            encoder.WriteUInt32(null, EnvelopeVersion);
            encoder.WriteInt32(null, (int)node.NodeClass);
            encoder.WriteByteString(null, new ByteString(nodeStream.ToArray()));
            WritePolicy(context, encoder, node, 0);
            encoder.Close();
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

        internal static HashSet<NodeId> GetDescendantNodeIds(ISystemContext context, NodeState node)
        {
            var ids = new HashSet<NodeId>();
            CollectDescendantNodeIds(context, node, ids);
            return ids;
        }

        private static void CollectDescendantNodeIds(ISystemContext context, NodeState node, HashSet<NodeId> ids)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (!child.NodeId.IsNull)
                {
                    ids.Add(child.NodeId);
                }
                CollectDescendantNodeIds(context, child, ids);
            }
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
            using var stream = new MemoryStream(bytes, writable: false);
            using var decoder = new BinaryDecoder(stream, context.AsMessageContext(), true);
            if (decoder.ReadUInt32(null) != EnvelopeMagic ||
                decoder.ReadUInt32(null) != EnvelopeVersion)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Distributed node payload requires a supported authorization envelope.");
            }

            var nodeClass = (NodeClass)decoder.ReadInt32(null);
            NodeState node = Create(nodeClass);
            ByteString nodePayload = decoder.ReadByteString(null);
            using var nodeStream = new MemoryStream(nodePayload.ToArray(), writable: false);
            node.LoadAsBinary(context, nodeStream);
            if (nodeStream.Position != nodeStream.Length ||
                ReadPolicyIdentity(decoder) != GetPolicyIdentity(context, node, StatusCodes.BadDecodingError))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Distributed node authorization does not match the node payload.");
            }
            RestorePolicy(context, decoder, node, 0);
            if (stream.Position != stream.Length)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Distributed node payload contains unexpected authorization data.");
            }
            return node;
        }

        /// <summary>
        /// Writes explicit policy states and stable identities without relying on replica namespace indexes.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The node uses caller-dependent authorization that cannot be represented by replicated attributes.
        /// </exception>
        private static void WritePolicy(
            ISystemContext context,
            BinaryEncoder encoder,
            NodeState node,
            int depth)
        {
            CheckPolicyDepth(encoder.Context, depth);
            if (node.OnReadRolePermissions != null ||
                node.OnReadUserRolePermissions != null ||
                node.OnReadAccessRestrictions != null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingError,
                    "Caller-dependent node authorization cannot be reconstructed from a replicated attribute.");
            }
            PolicyIdentity identity = GetPolicyIdentity(context, node, StatusCodes.BadEncodingError);
            encoder.WriteExpandedNodeId(null, identity.NodeId);
            encoder.WriteString(null, identity.BrowseName);
            encoder.WriteString(null, identity.NamespaceUri);
            encoder.WriteInt32(null, (int)identity.NodeClass);
            WriteRolePermissions(context, encoder, node.RolePermissions);
            WriteRolePermissions(context, encoder, node.UserRolePermissions);
            ValidateAccessRestrictions(node.AccessRestrictions, StatusCodes.BadEncodingError);
            encoder.WriteByte(null, node.AccessRestrictions.HasValue ? (byte)1 : (byte)0);
            if (node.AccessRestrictions.HasValue)
            {
                encoder.WriteUInt16(null, (ushort)node.AccessRestrictions.Value);
            }
            Dictionary<PolicyIdentity, BaseInstanceState> children =
                GetPolicyChildren(context, node, StatusCodes.BadEncodingError);
            encoder.WriteInt32(null, children.Count);
            foreach (BaseInstanceState child in children.Values)
            {
                WritePolicy(context, encoder, child, depth + 1);
            }
        }

        /// <summary>
        /// Requires a complete, uniquely matched policy tree before returning a reconstructed node.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The access-restriction discriminator is invalid, or the policy tree has missing or unmatched descendants.
        /// </exception>
        private static void RestorePolicy(
            ISystemContext context,
            BinaryDecoder decoder,
            NodeState node,
            int depth)
        {
            CheckPolicyDepth(decoder.Context, depth);
            node.RolePermissions = ReadRolePermissions(context, decoder);
            node.UserRolePermissions = ReadRolePermissions(context, decoder);
            byte hasRestrictions = decoder.ReadByte(null);
            if (hasRestrictions > 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Distributed node access restrictions have an invalid presence discriminator.");
            }
            node.AccessRestrictions = hasRestrictions == 0
                ? null
                : (AccessRestrictionType)decoder.ReadUInt16(null);
            ValidateAccessRestrictions(node.AccessRestrictions, StatusCodes.BadDecodingError);
            Dictionary<PolicyIdentity, BaseInstanceState> children =
                GetPolicyChildren(context, node, StatusCodes.BadDecodingError);
            if (decoder.ReadInt32(null) != children.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Distributed node authorization does not cover every descendant.");
            }
            int childCount = children.Count;
            for (int ii = 0; ii < childCount; ii++)
            {
                PolicyIdentity identity = ReadPolicyIdentity(decoder);
                if (!children.TryGetValue(identity, out BaseInstanceState? child) || child == null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "Distributed node authorization contains an unknown or duplicate descendant.");
                }
                children.Remove(identity);
                RestorePolicy(context, decoder, child, depth + 1);
            }
        }

        /// <summary>
        /// Writes null and empty role arrays distinctly and encodes role namespaces explicitly.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The role array exceeds the encoding limit or contains an invalid role.
        /// </exception>
        private static void WriteRolePermissions(
            ISystemContext context,
            BinaryEncoder encoder,
            ArrayOf<RolePermissionType> permissions)
        {
            if (encoder.Context.MaxArrayLength > 0 && permissions.Count > encoder.Context.MaxArrayLength)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Distributed node authorization exceeds the role array limit.");
            }
            encoder.WriteInt32(null, permissions.IsNull ? -1 : permissions.Count);
            foreach (RolePermissionType permission in permissions)
            {
                if (permission == null || permission.RoleId.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingError,
                        "Distributed node authorization contains an invalid role.");
                }
                GetNamespaceUri(context, permission.RoleId.NamespaceIndex, StatusCodes.BadEncodingError);
                encoder.WriteExpandedNodeId(null, NodeId.ToExpandedNodeId(permission.RoleId, context.NamespaceUris));
                encoder.WriteUInt32(null, permission.Permissions);
            }
        }

        /// <summary>
        /// Resolves every role locally or refuses the entire policy instead of dropping an entry.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The role array length is invalid, or a role namespace cannot be resolved locally.
        /// </exception>
        private static ArrayOf<RolePermissionType> ReadRolePermissions(
            ISystemContext context,
            BinaryDecoder decoder)
        {
            int count = decoder.ReadInt32(null);
            if (count == -1)
            {
                return default;
            }
            if (count < 0 || (decoder.Context.MaxArrayLength > 0 && count > decoder.Context.MaxArrayLength))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Distributed node authorization contains an invalid role array length.");
            }
            var permissions = new List<RolePermissionType>();
            for (int ii = 0; ii < count; ii++)
            {
                ExpandedNodeId portableId = decoder.ReadExpandedNodeId(null);
                var roleId = ExpandedNodeId.ToNodeId(portableId, context.NamespaceUris);
                if (roleId.IsNull ||
                    portableId.ServerIndex != 0 ||
                    (portableId.NamespaceIndex != 0 && string.IsNullOrEmpty(portableId.NamespaceUri)))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "Distributed node authorization requires an unavailable role namespace.");
                }
                permissions.Add(new RolePermissionType
                {
                    RoleId = roleId,
                    Permissions = decoder.ReadUInt32(null)
                });
            }
            return permissions.ToArrayOf();
        }

        /// <summary>
        /// Checks that every requested access restriction is understood by this replica.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The restrictions contain unsupported access-restriction bits.
        /// </exception>
        private static void ValidateAccessRestrictions(AccessRestrictionType? restrictions, StatusCode errorCode)
        {
            const AccessRestrictionType knownRestrictions =
                AccessRestrictionType.SigningRequired |
                AccessRestrictionType.EncryptionRequired |
                AccessRestrictionType.SessionRequired |
                AccessRestrictionType.ApplyRestrictionsToBrowse;
            if (restrictions.HasValue && (restrictions.Value & ~knownRestrictions) != AccessRestrictionType.None)
            {
                throw new ServiceResultException(
                    errorCode,
                    "Distributed node authorization contains unsupported access restrictions.");
            }
        }

        /// <summary>
        /// Bounds recursive policy processing by the configured encoding limits.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The policy depth exceeds the configured nesting limit.
        /// </exception>
        private static void CheckPolicyDepth(IServiceMessageContext context, int depth)
        {
            if (depth > context.MaxEncodingNestingLevels)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Distributed node authorization exceeds the nesting limit.");
            }
        }

        /// <summary>
        /// Identifies policy entries independently of child enumeration order or replica namespace indexes.
        /// </summary>
        private static PolicyIdentity GetPolicyIdentity(ISystemContext context, NodeState node, StatusCode errorCode)
        {
            GetNamespaceUri(context, node.NodeId.NamespaceIndex, errorCode);
            return new PolicyIdentity(
                NodeId.ToExpandedNodeId(node.NodeId, context.NamespaceUris),
                node.BrowseName.Name,
                GetNamespaceUri(context, node.BrowseName.NamespaceIndex, errorCode),
                node.NodeClass);
        }

        /// <summary>
        /// Reads the identity bound to one policy entry.
        /// </summary>
        private static PolicyIdentity ReadPolicyIdentity(BinaryDecoder decoder)
        {
            return new PolicyIdentity(
                decoder.ReadExpandedNodeId(null),
                decoder.ReadString(null),
                decoder.ReadString(null) ?? string.Empty,
                (NodeClass)decoder.ReadInt32(null));
        }

        /// <summary>
        /// Prevents an unresolved namespace index from becoming another replica's local namespace.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The namespace index does not identify an available namespace URI.
        /// </exception>
        private static string GetNamespaceUri(ISystemContext context, ushort namespaceIndex, StatusCode errorCode)
        {
            string? namespaceUri = context.NamespaceUris.GetString(namespaceIndex);
            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw new ServiceResultException(
                    errorCode,
                    "Distributed node authorization requires an unavailable namespace.");
            }
            return namespaceUri;
        }

        /// <summary>
        /// Requires an unambiguous policy identity for every direct child.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Multiple descendants have the same policy identity.
        /// </exception>
        private static Dictionary<PolicyIdentity, BaseInstanceState> GetPolicyChildren(
            ISystemContext context,
            NodeState node,
            StatusCode errorCode)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            var indexed = new Dictionary<PolicyIdentity, BaseInstanceState>();
            foreach (BaseInstanceState child in children)
            {
                if (!indexed.TryAdd(GetPolicyIdentity(context, child, errorCode), child))
                {
                    throw new ServiceResultException(
                        errorCode,
                        "Distributed node authorization cannot identify a duplicate descendant.");
                }
            }
            return indexed;
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
            Dictionary<PolicyIdentity, BaseInstanceState> updatedChildren =
                GetPolicyChildren(context, existing.Node, StatusCodes.BadDecodingError);

            foreach (BaseInstanceState sourceChild in sourceChildren)
            {
                ExistingNodeSnapshot? existingChild = FindExistingChild(
                    existing,
                    sourceChild);
                if (existingChild == null)
                {
                    if (!updatedChildren.TryGetValue(
                        GetPolicyIdentity(context, sourceChild, StatusCodes.BadDecodingError),
                        out BaseInstanceState? addedChild) ||
                        addedChild == null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError,
                            "An updated node did not reconstruct a descendant required by its authorization policy.");
                    }
                    CopyPolicyTree(context, addedChild, sourceChild);
                    continue;
                }

                UpdateExistingNode(context, existingChild, sourceChild);
                var child = (BaseInstanceState)existingChild.Node;
                child.Parent = existing.Node;
                ReplaceChild(context, existing.Node, child);
            }
        }

        /// <summary>
        /// Restores policy on newly initialized children, including children created by generated factories.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The reconstructed descendants do not match the source authorization policy tree.
        /// </exception>
        private static void CopyPolicyTree(ISystemContext context, NodeState target, NodeState source)
        {
            target.RolePermissions = source.RolePermissions;
            target.UserRolePermissions = source.UserRolePermissions;
            target.AccessRestrictions = source.AccessRestrictions;
            Dictionary<PolicyIdentity, BaseInstanceState> targetChildren =
                GetPolicyChildren(context, target, StatusCodes.BadDecodingError);
            Dictionary<PolicyIdentity, BaseInstanceState> sourceChildren =
                GetPolicyChildren(context, source, StatusCodes.BadDecodingError);
            if (targetChildren.Count != sourceChildren.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "An updated node did not reconstruct every descendant required by its authorization policy.");
            }
            foreach ((PolicyIdentity identity, BaseInstanceState sourceChild) in sourceChildren)
            {
                if (!targetChildren.TryGetValue(identity, out BaseInstanceState? targetChild) || targetChild == null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "An updated descendant does not match its authorization policy.");
                }
                CopyPolicyTree(context, targetChild, sourceChild);
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
            target.RolePermissions = source.RolePermissions;
            target.UserRolePermissions = source.UserRolePermissions;
            target.AccessRestrictions = source.AccessRestrictions;

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

        /// <summary>
        /// Magic identifying the replication-only authorization envelope ("NSRP").
        /// </summary>
        private const uint EnvelopeMagic = 0x5052534E;

        /// <summary>
        /// The first envelope version requires a policy entry for every serialized node.
        /// </summary>
        private const uint EnvelopeVersion = 1;

        /// <summary>
        /// The portable identity of a node's authorization policy.
        /// </summary>
        private readonly record struct PolicyIdentity(
            ExpandedNodeId NodeId,
            string? BrowseName,
            string NamespaceUri,
            NodeClass NodeClass);

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
