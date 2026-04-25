#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.Obsolete
{
    using Opc.Ua;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Wrapper to intercept undesired calls on the system context
    /// </summary>
    internal class TypeTree : ITypeTable
    {
        /// <summary>
        /// Type table
        /// </summary>
        /// <param name="cache"></param>
        public TypeTree(INodeCache cache) => _cache = cache;

        /// <inheritdoc/>
        public NodeId FindSuperType(ExpandedNodeId typeId)
        {
            throw NotSupported(nameof(FindSuperType));
        }

        /// <inheritdoc/>
        public NodeId FindSuperType(NodeId typeId)
        {
            throw NotSupported(nameof(FindSuperType));
        }

        /// <inheritdoc/>
        public Task<NodeId> FindSuperTypeAsync(ExpandedNodeId typeId,
            CancellationToken ct)
        {
            throw NotSupported(nameof(FindSuperTypeAsync));
        }

        /// <inheritdoc/>
        public Task<NodeId> FindSuperTypeAsync(NodeId typeId, CancellationToken ct)
        {
            throw NotSupported(nameof(FindSuperTypeAsync));
        }

        /// <inheritdoc/>
        public bool IsTypeOf(ExpandedNodeId subTypeId, ExpandedNodeId superTypeId)
        {
            throw NotSupported(nameof(IsTypeOf));
        }

        /// <inheritdoc/>
        public bool IsTypeOf(NodeId subTypeId, NodeId superTypeId)
        {
            throw NotSupported(nameof(IsTypeOf));
        }

        /// <inheritdoc/>
        public bool IsKnown(ExpandedNodeId typeId)
        {
            throw NotSupported(nameof(IsKnown));
        }

        /// <inheritdoc/>
        public bool IsKnown(NodeId typeId)
        {
            throw NotSupported(nameof(IsKnown));
        }

        /// <inheritdoc/>
        public ArrayOf<NodeId> FindSubTypes(ExpandedNodeId typeId)
        {
            throw NotSupported(nameof(FindSubTypes));
        }

        /// <inheritdoc/>
        public QualifiedName FindReferenceTypeName(NodeId referenceTypeId)
        {
            throw NotSupported(nameof(FindReferenceTypeName));
        }

        /// <inheritdoc/>
        public NodeId FindReferenceType(QualifiedName browseName)
        {
            throw NotSupported(nameof(FindReferenceType));
        }

        /// <inheritdoc/>
        public bool IsEncodingOf(ExpandedNodeId encodingId, ExpandedNodeId datatypeId)
        {
            throw NotSupported(nameof(IsEncodingOf));
        }

        /// <inheritdoc/>
        public bool IsEncodingFor(NodeId expectedTypeId, ExtensionObject value)
        {
            throw NotSupported(nameof(IsEncodingFor));
        }

        /// <inheritdoc/>
        public bool IsEncodingFor(NodeId expectedTypeId, Variant value)
        {
            throw NotSupported(nameof(IsEncodingFor));
        }

        /// <inheritdoc/>
        public NodeId FindDataTypeId(ExpandedNodeId encodingId)
        {
            throw NotSupported(nameof(FindDataTypeId));
        }

        /// <inheritdoc/>
        public NodeId FindDataTypeId(NodeId encodingId)
        {
            throw NotSupported(nameof(FindDataTypeId));
        }

        /// <summary>
        /// Throw not supported exception
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private static ServiceResultException NotSupported(string name)
        {
#if DEBUG_OBSOLETE
            System.Diagnostics.Debug.Fail(name + " not supported");
#endif
            return ServiceResultException.Create(StatusCodes.BadNotSupported,
                name + " deprecated");
        }

        private readonly INodeCache _cache;
    }
}
#endif
