/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

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
