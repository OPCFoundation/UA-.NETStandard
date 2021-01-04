/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.IO;
using System.Runtime.Serialization;
using System.Reflection;
using System.Threading;

namespace Opc.Ua
{       
    /// <summary> 
    /// The base class for all object type nodes.
    /// </summary>
    public class BaseObjectTypeState : BaseTypeState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public BaseObjectTypeState() : base(NodeClass.ObjectType)
        {
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SuperTypeId = Opc.Ua.NodeId.Create(Opc.Ua.ObjectTypes.BaseObjectType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            NodeId = Opc.Ua.NodeId.Create(Opc.Ua.ObjectTypes.BaseObjectType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            BrowseName = Opc.Ua.QualifiedName.Create(Opc.Ua.BrowseNames.BaseObjectType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            DisplayName = new LocalizedText(Opc.Ua.BrowseNames.BaseObjectType, String.Empty, Opc.Ua.BrowseNames.BaseObjectType);
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
        #endregion
    }
    
    /// <summary> 
    /// The base class for all object type nodes.
    /// </summary>
    public class FolderTypeState : BaseObjectTypeState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public FolderTypeState()
        {
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SuperTypeId = Opc.Ua.NodeId.Create(Opc.Ua.ObjectTypes.FolderType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            NodeId = Opc.Ua.NodeId.Create(Opc.Ua.ObjectTypes.FolderType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            BrowseName = Opc.Ua.QualifiedName.Create(Opc.Ua.BrowseNames.FolderType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            DisplayName = new LocalizedText(Opc.Ua.BrowseNames.FolderType, String.Empty, Opc.Ua.BrowseNames.FolderType);
            Description = null;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            IsAbstract = false;
        }
        #endregion
    }
}
