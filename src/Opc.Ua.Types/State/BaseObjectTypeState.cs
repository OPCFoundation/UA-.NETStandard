// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// The base class for all object type nodes.
    /// </summary>
    public class BaseObjectTypeState : BaseTypeState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        public BaseObjectTypeState()
            : base(NodeClass.ObjectType)
        {
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SuperTypeId = NodeId.Create(
                ObjectTypes.BaseObjectType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            NodeId = NodeId.Create(
                ObjectTypes.BaseObjectType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            BrowseName = QualifiedName.Create(
                BrowseNames.BaseObjectType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            DisplayName = new LocalizedText(
                BrowseNames.BaseObjectType,
                string.Empty,
                BrowseNames.BaseObjectType);
            Description = default;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            IsAbstract = false;
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new BaseObjectTypeState();
        }

        /// <inheritdoc/>
        protected override NodeState CreateCopy()
        {
            return new BaseObjectTypeState();
        }
    }
}
