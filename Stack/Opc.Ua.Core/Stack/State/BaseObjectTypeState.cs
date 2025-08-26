/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;

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
            Description = null;
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
        public override object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Makes a copy of the node and all children.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            var clone = (BaseObjectTypeState)Activator.CreateInstance(GetType());
            return CloneChildren(clone);
        }
    }

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
                ObjectTypes.FolderType,
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
            Description = null;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            IsAbstract = false;
        }
    }
}
