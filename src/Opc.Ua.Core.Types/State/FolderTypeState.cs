/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// The base class for all object type nodes.
    /// </summary>
    public class FolderTypeState : BaseObjectTypeState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        public FolderTypeState()
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
            NodeId = NodeId.Create(ObjectTypes.FolderType, Namespaces.OpcUa, context.NamespaceUris);
            BrowseName = QualifiedName.Create(
                BrowseNames.FolderType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            DisplayName = new LocalizedText(
                BrowseNames.FolderType,
                string.Empty,
                BrowseNames.FolderType);
            Description = default;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            IsAbstract = false;
        }

        /// <inheritdoc/>
        protected override NodeState CreateCopy()
        {
            return new FolderTypeState();
        }
    }
}
