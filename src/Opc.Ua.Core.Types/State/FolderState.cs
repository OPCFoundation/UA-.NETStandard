/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// The base class for all folder nodes.
    /// </summary>
    public class FolderState : BaseObjectState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        public FolderState(NodeState? parent)
            : base(parent)
        {
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SymbolicName = CoreUtils.Format("{0}_Instance1", BrowseNames.FolderType);
            NodeId = default;
            BrowseName = new QualifiedName(SymbolicName, 1);
            DisplayName = new LocalizedText(SymbolicName);
            Description = default;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            TypeDefinitionId = GetDefaultTypeDefinitionId(context.NamespaceUris);
            NumericId = ObjectTypes.FolderType;
            EventNotifier = EventNotifiers.None;
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return ObjectTypeIds.FolderType;
        }

        /// <inheritdoc/>
        protected override NodeState CreateCopy()
        {
            return new FolderState(Parent);
        }
    }
}
