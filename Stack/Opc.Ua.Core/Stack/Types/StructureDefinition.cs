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
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    #region StructureDefinition Class
    /// <summary>
    /// 
    /// </summary>
    /// <exclude />
    public partial class StructureDefinition : DataTypeDefinition
    {
        /// <summary>
        /// Set the default encoding id for the requested data encoding.
        /// </summary>
        /// <param name="context">The system context with the encodeable factory.</param>
        /// <param name="typeId">The type id of the Data Type.</param>
        /// <param name="dataEncoding">The data encoding to apply to the default encoding id.</param>
        public void SetDefaultEncodingId(ISystemContext context, NodeId typeId, QualifiedName dataEncoding)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (dataEncoding?.Name == BrowseNames.DefaultJson)
            {
                DefaultEncodingId = ExpandedNodeId.ToNodeId(typeId, context.NamespaceUris);
                return;
            }

            // note: custom types must be added to the encodeable factory by the node manager to be found
            var systemType = context.EncodeableFactory?.GetSystemType(NodeId.ToExpandedNodeId(typeId, context.NamespaceUris));
            if (systemType != null && Activator.CreateInstance(systemType) is IEncodeable encodeable)
            {
                if (dataEncoding == null || dataEncoding.Name == BrowseNames.DefaultBinary)
                {
                    DefaultEncodingId = ExpandedNodeId.ToNodeId(encodeable.BinaryEncodingId, context.NamespaceUris);
                }
                else if (dataEncoding.Name == BrowseNames.DefaultXml)
                {
                    DefaultEncodingId = ExpandedNodeId.ToNodeId(encodeable.XmlEncodingId, context.NamespaceUris);
                }
            }
        }
    }
    #endregion
}
