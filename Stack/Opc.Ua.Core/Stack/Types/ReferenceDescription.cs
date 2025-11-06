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

namespace Opc.Ua
{
    /// <summary>
    /// Extends reference description with helper functionality
    /// </summary>
    public static class ReferenceDescriptionExtensions
    {
        /// <summary>
        /// Sets the reference type for the reference.
        /// </summary>
        public static void SetReferenceType(
            this ReferenceDescription referenceDescription,
            BrowseResultMask resultMask,
            NodeId referenceTypeId,
            bool isForward)
        {
            if (((int)resultMask & (int)BrowseResultMask.ReferenceTypeId) != 0)
            {
                referenceDescription.ReferenceTypeId = referenceTypeId;
            }
            else
            {
                referenceDescription.ReferenceTypeId = null;
            }

            if (((int)resultMask & (int)BrowseResultMask.IsForward) != 0)
            {
                referenceDescription.IsForward = isForward;
            }
            else
            {
                referenceDescription.IsForward = false;
            }
        }

        /// <summary>
        /// Sets the target attributes for the reference.
        /// </summary>
        public static void SetTargetAttributes(
            this ReferenceDescription referenceDescription,
            BrowseResultMask resultMask,
            NodeClass nodeClass,
            QualifiedName browseName,
            LocalizedText displayName,
            ExpandedNodeId typeDefinition)
        {
            if (((int)resultMask & (int)BrowseResultMask.NodeClass) != 0)
            {
                referenceDescription.NodeClass = nodeClass;
            }
            else
            {
                referenceDescription.NodeClass = 0;
            }

            if (((int)resultMask & (int)BrowseResultMask.BrowseName) != 0)
            {
                referenceDescription.BrowseName = browseName;
            }
            else
            {
                referenceDescription.BrowseName = null;
            }

            if (((int)resultMask & (int)BrowseResultMask.DisplayName) != 0)
            {
                referenceDescription.DisplayName = displayName;
            }
            else
            {
                referenceDescription.DisplayName = null;
            }

            if (((int)resultMask & (int)BrowseResultMask.TypeDefinition) != 0)
            {
                referenceDescription.TypeDefinition = typeDefinition;
            }
            else
            {
                referenceDescription.TypeDefinition = null;
            }
        }
    }
}
